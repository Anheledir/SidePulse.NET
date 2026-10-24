using System.ComponentModel;
using QRCoder;
using SidePulse.Core;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SidePulse.Cli;

public static class PhoneStores
{
    public static LinkStore Open() => new(LinkStore.DefaultPath, PlatformSecrets.Create(LinkStore.DefaultPath));
}
public class LinkListSettings : CommandSettings
{
    [CommandOption("--json")]
    public bool Json { get; init; }
}
public sealed class LinkListCommand : Command<LinkListSettings>
{
    public override int Execute(CommandContext context, LinkListSettings settings, CancellationToken cancellationToken)
    {
        var phones = PhoneStores.Open().List();
        if (settings.Json) Output.Print(phones);
        else
        {
            var table = new Table().Border(TableBorder.Rounded).AddColumn("Alias").AddColumn("Phone").AddColumn("Default");
            foreach (var phone in phones)
                table.AddRow(Markup.Escape(phone.Alias), Markup.Escape(phone.Name), phone.IsDefault ? "yes" : "");
            AnsiConsole.Write(table);
            if (phones.Count == 0) AnsiConsole.WriteLine("No saved phones. Use link pair to connect an iPhone.");
        }
        return 0;
    }
}
public class AliasSettings : CommandSettings
{
    [CommandArgument(0, "<ALIAS>")]
    public string Alias { get; init; } = "";
    public override ValidationResult Validate()
    {
        try { PhoneTokens.Alias(Alias); return ValidationResult.Success(); }
        catch (ArgumentException ex) { return ValidationResult.Error(ex.Message); }
    }
}
public sealed class DefaultPhoneCommand : Command<AliasSettings>
{
    public override int Execute(CommandContext context, AliasSettings settings, CancellationToken cancellationToken)
    {
        PhoneStores.Open().SetDefault(settings.Alias);
        AnsiConsole.WriteLine("Default phone updated.");
        return 0;
    }
}
public sealed class RemovePhoneCommand : Command<AliasSettings>
{
    public override int Execute(CommandContext context, AliasSettings settings, CancellationToken cancellationToken)
    {
        PhoneStores.Open().Remove(settings.Alias);
        AnsiConsole.WriteLine("Saved phone removed. This removes the local link; it does not revoke the token at the bridge.");
        return 0;
    }
}
public sealed class RenameSettings : AliasSettings
{
    [CommandArgument(1, "<NEW-ALIAS>")]
    public string NewAlias { get; init; } = "";
    public override ValidationResult Validate()
    {
        var original = base.Validate();
        if (!original.Successful) return original;
        try { PhoneTokens.Alias(NewAlias); return ValidationResult.Success(); }
        catch (ArgumentException ex) { return ValidationResult.Error(ex.Message); }
    }
}
public sealed class RenamePhoneCommand : Command<RenameSettings>
{
    public override int Execute(CommandContext context, RenameSettings settings, CancellationToken cancellationToken)
    {
        PhoneStores.Open().Rename(settings.Alias, settings.NewAlias);
        AnsiConsole.WriteLine("Phone alias updated.");
        return 0;
    }
}
public sealed class ImportPhoneSettings : AliasSettings
{
    [CommandOption("--token-env <NAME>")]
    [Description("Environment variable holding an existing iPhone push token. The value is never printed.")]
    public string TokenEnvironment { get; init; } = "SIDEPULSE_TOKEN";
    [CommandOption("--name <NAME>")]
    public string Name { get; init; } = "iPhone";
}
public sealed class ImportPhoneCommand : Command<ImportPhoneSettings>
{
    public override int Execute(CommandContext context, ImportPhoneSettings settings, CancellationToken cancellationToken)
    {
        var store = PhoneStores.Open();
        var token = Environment.GetEnvironmentVariable(settings.TokenEnvironment);
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("The selected token environment variable is empty.");
        store.Add(new(settings.Alias, settings.Name, token, DateTimeOffset.UtcNow));
        AnsiConsole.MarkupLine("[green]Phone saved in the protected store.[/]");
        return 0;
    }
}
public sealed class PairPhoneSettings : AliasSettings
{
    [CommandOption("--timeout <SECONDS>")]
    [DefaultValue(300)]
    public int TimeoutSeconds { get; init; } = 300;
    [CommandOption("--no-qr")]
    [Description("Show only the pairing URI, for terminals that cannot render the QR code.")]
    public bool NoQr { get; init; }
    public override ValidationResult Validate()
    {
        var original = base.Validate();
        if (!original.Successful) return original;
        return TimeoutSeconds is >= 1 and <= 300 ? ValidationResult.Success() : ValidationResult.Error("Pairing timeout must be 1–300 seconds.");
    }
}
public sealed class PairPhoneCommand : AsyncCommand<PairPhoneSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PairPhoneSettings settings, CancellationToken cancellationToken)
    {
        var store = PhoneStores.Open();
        if (store.List().Any(p => p.Alias.Equals(settings.Alias, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("That phone alias already exists. Remove it or choose a different alias.");
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; shutdown.Cancel(); };
        Console.CancelKeyPress += cancel;
        var channel = Pairing.NewChannel();
        var url = Pairing.Url(channel);
        try
        {
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            // Start subscribing before presenting the QR to reduce registration races.
            var pairing = Pairing.WaitAsync(client, channel, settings.Alias, TimeSpan.FromSeconds(settings.TimeoutSeconds), shutdown.Token);
            try
            {
                AnsiConsole.Write(new Rule("[cyan]Link iPhone[/]").LeftJustified());
                AnsiConsole.WriteLine("Scan with the SidePulse app, or open this pairing link on your phone:");
                AnsiConsole.WriteLine(url);
                if (!settings.NoQr && !Console.IsOutputRedirected)
                {
                    using var code = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.M);
                    if (AnsiConsole.Profile.Capabilities.Ansi && AnsiConsole.Profile.Width >= code.ModuleMatrix.Count * 2)
                    {
                        foreach (var row in code.ModuleMatrix)
                        {
                            AnsiConsole.MarkupLine(string.Concat(row.Cast<bool>().Select(dark =>
                                dark ? "[white on black]  [/]" : "[black on white]  [/]")));
                        }
                    }
                    else AnsiConsole.WriteLine("QR rendering needs an ANSI terminal with sufficient width. Use the pairing link above.");
                }
                AnsiConsole.WriteLine($"Waiting up to {settings.TimeoutSeconds} seconds. Ctrl+C cancels.");
                var phone = await pairing;
                store.Add(phone);
                AnsiConsole.MarkupLine("[green]Linked and saved securely:[/] " + Markup.Escape(phone.Name));
                AnsiConsole.WriteLine($"Try: sidepulse-net state done --phone {phone.Alias}");
                return 0;
            }
            finally
            {
                shutdown.Cancel();
                try { await pairing; } catch (Exception ex) when (ex is OperationCanceledException or TimeoutException) { }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            AnsiConsole.WriteLine("Pairing cancelled. No phone was saved.");
            return 0;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }
}

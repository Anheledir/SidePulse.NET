> Implementation update: QR/SSE pairing for the default iPhone bridge and encrypted
> native link storage are implemented on Windows, macOS and Linux. Android and custom bridge origins remain planned.
> Hardware and TestFlight app verification are still pending. See [[Getting-Started]].

# Devices and protocols

## Local hardware

SidePulse devices accept a plain-text LED program through `LEDS.LED` on compatible
mounted volumes. Preserve upstream syntax, validation and byte limits before
writing. Use explicit destination selection when discovery is ambiguous.

Windows discovery should identify volumes without recursively scanning disks.
Handle removal, read-only media, interrupted writes and concurrent access. Raw USB
and read-only transports are separate work and must not be implied by file-write support.

## Phones

Reuse the original pairing and bridge protocol instead of inventing a new one.
Document the selected bridge origin and target identity. Store credentials using
current-user protection where feasible, with an explicit secure migration path.

Silent LED updates and visible notifications are separate intents. Check the actual
upstream implementation: do not assume command names alone define notification
behavior. Preserve production/development and Android token distinctions.

## Delivery evidence

| Evidence | Meaning |
|---|---|
| Queued locally | Host accepted the output request |
| Written and flushed to volume | OS accepted the file write; firmware acknowledgement may be unavailable |
| Bridge accepted | Bridge accepted the phone request |
| Phone/device acknowledgement | Only show this when the protocol provides real confirmation |

Retry bounded transient failures with deduplication and cancellation. Never endlessly
replay an outdated working state after a newer waiting/completed state was observed.
A later migration or relay feature must preserve source machine identity.

Protocol fixtures should be derived from the pinned upstream source revision and
redacted captures, with attribution recorded alongside them.

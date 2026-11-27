# tinyxnb privacy policy

*Effective 27 November 2026. Publisher: Parker Wahle, regulad@regulad.xyz.*

tinyxnb gives Windows Explorer thumbnails for `.xnb` files, and a **Save as PNG** command, `xnb2png`, that converts them. It comes as two programs: the thumbnail handler, which Explorer runs, and `xnb2png`.

## Your files stay on this computer

tinyxnb reads your `.xnb` files and writes the PNGs you ask for, next to them or where you choose. It doesn't send your files, their contents, their names or where they are anywhere.

## Diagnostics

To find and fix problems, tinyxnb's releases send crash reports and usage counts (a build of tinyxnb from its source code sends none) to Sentry (Functional Software, Inc., sentry.io), a service that stores them on our behalf, in the United States:

- **Errors and crash reports:** what went wrong and where in tinyxnb's code, with the error's message stripped of file paths and of your user and computer names. Only what points at a fault in tinyxnb: not a file that isn't a texture, or one that can't be read or written.
- **Runs:** one record for each run of a program (each start of the process that shows thumbnails, and each run of `xnb2png`): when it started, whether it hit an error, and, for `xnb2png`, whether it finished or crashed.
- **Usage counts:** one record for each thumbnail the handler makes, with the size Explorer asked for, the texture's format (for example `Dxt5`) and how it went; and one for each run of `xnb2png`, with how it was run (on files, a folder, or for information), whether Explorer's **Save as PNG** started it, how many files it converted, skipped and failed, and how it went.
- **This computer:** tinyxnb's version, the Windows version and the .NET version, and with an error report its time zone.

Each run of a program (each start of the process that shows thumbnails, and each run of `xnb2png`) gets a random ID, made when it starts and never stored, so that a crash can be told apart from another run's. tinyxnb doesn't send names (yours or this computer's), file names or paths, file contents, user, account or device IDs, network (MAC or IP) addresses, or anything else that identifies you or this computer, and our diagnostics service is set not to store IP addresses. It does note roughly where each report came from (the city, region and country), which it works out from the connection before discarding the address.

This is used only to diagnose and improve tinyxnb. It isn't sold, shared for advertising, or combined with other data. Sentry keeps it for up to 90 days.

## Turning diagnostics off

Set the environment variable `DO_NOT_TRACK` to `1` (any value but empty, `0` or `false` will do), for instance with `setx DO_NOT_TRACK 1`, and tinyxnb sends nothing. Each program reads it when it starts:

- **`xnb2png`** sees it from its next run.
- **The thumbnail handler** runs in a process Windows starts for Explorer, which sees a new setting only once it is started again: sign out and back in, or restart Explorer.

## Children

tinyxnb isn't directed at children and doesn't knowingly collect children's personal information.

## Contact

Questions: regulad@regulad.xyz.

## Changes

We'll update this page, and its effective date, if any of this changes.

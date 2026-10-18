# Remaining manual checks

Automated fixtures use their own capture windows, temporary settings, native hotkey messages and simulated audio notification inputs. The following checks cover real keyboard focus, sound-device hardware and Windows presentation. They require a person operating the target PC and remain pending until their results are recorded.

## User acceptance (recorded 2026-10-18)

The user initially reported that the core workflow worked, with occasional chipped audio in saved clips at Windows output volume 80–90 and a yellow recording outline. After receiving the updated portable build with audio corrections and full-monitor capture without an outline, the user confirmed: “Alright it works now.” The reported audio and outline issues are accepted as resolved by the user. No further implementation is pending for those issues.

Accepted build: `Rewind-win-x64-preview-e0fc614c`, verified locally before the user's confirmation. The focused recheck concerned **Full monitor (no outline)** and saved audio; **Window or display** intentionally retains the Windows capture border. Saved audio is about 6 dB quieter. Existing saved clips were unchanged. General acceptance does not individually certify the physical-device, keyboard-focus or presentation checks below; those are retained as unverified checks rather than additional feature work.

## Keyboard, sound and tray presentation

1. Run `scripts/run.ps1`. Start buffering, choose a real application or monitor, and play a short audio/video sample through the normal speakers/headphones.
2. Minimize Rewind. Focus a different application and press Ctrl+Shift+F8 once (use Fn too if your keyboard requires it for function keys). Confirm that a clip appears in the configured folder. If a shortcut conflict is reported, select one of the F9/F10 alternatives first.
3. Play the clip. Check that the expected pre-trigger screen activity and audible system output are present and appear synchronized. Windows notification settings can suppress the save balloon; the clip and saved-status text are the authoritative result.
4. Restore Rewind through the tray and confirm its status is readable. Start/Stop and Exit should respond from the tray; Exit should remove the icon.

## Physical playback-device changes

1. Start a fresh audio-enabled session. Change Windows' default multimedia output, or disconnect the headphones/USB output currently being captured.
2. Confirm that buffering stops with an actionable playback-device message. Existing saved clips must remain playable. Unsaved rolling history is discarded when the failed session is cleaned up.
3. Reconnect/select a working output and start buffering again. Save and play another clip to confirm audio uses the current output.

## Target application and display conditions

Check the application intended for the presentation, especially fullscreen content and any resolution/DPI/display changes used during the demonstration. Resizing a selected window currently ends the session intentionally; restart after resizing. Lock/unlock, sleep/resume, protected content and exclusive fullscreen behavior have not been certified by the controlled-window fixtures. Record any differences before using those conditions in a demonstration.

Disk-full, forced driver/GPU reset and unplugging the monitor were not induced on the user's machine. Routine save errors are covered automatically using a destination that cannot be a directory and an exclusively locked file. Additional destructive fault injection is not required to try the app.

Record the date, Windows version, output device, selected source and result here or in a separate local test note. Passing automated fixtures alone does not mark the physical-device or presentation checks complete.

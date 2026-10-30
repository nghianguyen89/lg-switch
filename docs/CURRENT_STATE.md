# Current state

- User confirmed v0.3 switches LG from DP to HDMI and back to DP while NUC returns to Extend. Hardware: NUC, LG UP600 on DP, ASUS VA24EHF on HDMI; Windows laptop/device on LG HDMI. Exact tested HDMI-2 round trip was not separately recorded.
- v1.0.1 fixes partial redraw text/background ghosts in the dark native UI. Both buttons fully repaint their intersected clip and use managed text/focus drawing. Physical-image confirmation popups remain removed at the user's explicit request; DP remains recovery. Intel transport/preflight and driver settling times are unchanged.
- Production manifest now requires administrator at launch. UAC still belongs to Windows; no settings are changed to suppress it. Click-time elevation/relaunch code is removed. A separate asInvoker developer entry point under obj/checks is used for self-tests/PE-manifest inspection/UI-only preview and is not shipped in the portable ZIP.
- VCP60 input readback can be0. UI may show the last requested input but must label it as a request, not monitor-confirmed state.
- Intel provider uses exact LUID + target matching and System32 DLL loading. Brightness GET response must validate and match Win32 before SET. AUX framing is inferred; runtime GET preflight is required.
- Forward: send HDMI then apply ASUS-only. Return: restore Extend before sending DP. Never remove ASUS. Clone/extra-display configurations are refused.
- DDC worker timeout8s; independent recovery watchdog90s. Keep recovery armed after failed immediate recovery. Native calls and topology writes share an I/O mutex.
- Build: build-publish.ps1, .NET Framework4.8 C#5 x64. No external packages. Source root is this folder; release/, settings, recovery and logs stay out of Git.
- Current checks and limits are in VALIDATION.md. A rendered UI preview and pure tests do not prove the newest binary on hardware.

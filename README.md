# sys-hidplus-client-lite

Lightweight Windows GUI client for sys-hidplus.

- Uses XInput controllers (Xbox-compatible).
- Very small footprint: single `.exe`, no installer, no external dependencies.
- Saves the last IP in Windows user data:
  `%LocalAppData%\sys-hidplus-client-lite\settings.json`.

## Why this project

I found these projects and they are cool:
- https://github.com/kenesu-h/sys-hidplus-client-rs
- https://github.com/luvvyamy/SwitchSysHidplusClient

But on my side they were not consistently reliable on every Windows PC.
So I made this version using default Windows libraries only, to improve compatibility and also made a fix to avoid ghost controllers (which needed restarting the entire console bruh).

License: MIT

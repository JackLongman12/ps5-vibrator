# Security

## Reporting a problem

If you find a security issue, please report it privately via **GitHub → Security → Report a vulnerability** on this repository. Please don't open a public issue for it.

## How the app handles risk

| Area | What the app does |
|---|---|
| **Updates** | Updates are downloaded only from this repository's GitHub Releases over HTTPS. Velopack verifies each package's checksum before installing it. The update feed can only point at an `https://github.com/{owner}/{repo}` URL. |
| **Tokens** | No token is needed, because the release repository is public. If you point updates at a private fork, you can save a token in Settings. It's encrypted with Windows DPAPI for your user account and is only ever sent to GitHub. |
| **Pattern files** | Imported `*.dspattern.json` files are treated as untrusted. The app enforces a size limit (8 MB), a JSON depth limit, a maximum length (10 minutes), and clamped sample rates and values. A pattern's id has to be a plain hex id, so a crafted file can't write outside the library folder. |
| **Controller** | The app only opens Sony DualSense / DualSense Edge HID devices (VID `054C`, PID `0CE6`/`0DF2`). |
| **Physical safety** | There's a global maximum-intensity cap and an optional auto-stop timer. You can always stop everything with `Esc`, `Ctrl+Alt+S` from any app, or the controller's mute button. |
| **Releases** | `scripts/release.ps1` runs the tests before publishing. It passes the GitHub token to the Velopack CLI through an environment variable, never as a command-line argument. |

## Known limitations

- Builds aren't code-signed yet, so Windows SmartScreen shows a warning the first time you install.
- Anyone with write access to this repository can publish a release, and installed copies will download it. Protect the account with 2FA.

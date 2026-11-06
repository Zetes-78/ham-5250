# Ham 5250

A Hamrick's-branded 5250 terminal for IBM i (AS/400), built as a native Windows 11 app.
It doesn't need IBM software or Java: everything is in one `Ham5250.exe`.

Built by Jason Scates · [Night Tech Solutions](https://nighttech.solutions)

## Download

Go to **Releases** and download either:

- `Ham5250-Setup-x.y.z.exe`: the installer. It adds a Start menu entry and asks for the IBM i host.
- `Ham5250-Portable-x.y.z.zip`: a single exe you can run with no install.

The builds aren't code-signed yet, so Windows SmartScreen may warn on first run (More info → Run anyway).

## Features

**Connection**
- TN5250E over **TLS (port 992)**, or plain telnet on port 23
- Certificate pinning by thumbprint for a self-signed IBM i certificate
- **IBM i device names** made automatically from the PC name (`H` + PC name, 10 characters max), or set by hand
  - Each extra session gets its own device name (…2, …3)
- RFC 4777 auto-configuration: device name, code page and character set
- **Auto-reconnect** with back-off, so the sign-on screen comes back after a sign-off or network drop
- **Multiple sessions** in tabs (Ctrl+T)
- Multiple saved profiles, for different systems or partitions

**Screen**
- 24×80 and **27×132** screens
- Full 5250 color set, with reverse, underline, column separators and non-display (password) fields
- Text scales to fit the window, with full screen on Alt+Enter
- Status line: connection and TLS state, device name, keyboard lock (`X SYSTEM`), insert mode, message waiting, cursor position

**Keyboard**
- Two layouts:
  - **Windows style:** Enter sends, Right Ctrl = Field Exit
  - **Classic PC5250:** Right Ctrl sends, Enter = Field Exit
- Field Exit, Field+, Field- (keypad + / -), signed numeric fields, right-adjust zero and blank fill
- Field types enforced: numeric only, digits only, alpha only, monocase, bypass, auto-enter and Field Exit required
- Insert mode, Erase EOF, Erase Input, Dup, Reset (Left Ctrl), Attention (Esc), System Request (Shift+Esc), Help, Clear
- **Type-ahead:** keys pressed while the IBM i is busy are kept and played back when it's ready
- An on-screen **function key bar** (F1–F24, Enter, Page Up/Down, Attn, Sys Req, Reset…) for mouse or touch screens

**Mouse and clipboard**
- Click to place the cursor
- **Click "F3=Exit"-style text** on screen to press that key
- Drag to select a rectangle and Ctrl+C to copy (or copy the whole screen)
- Ctrl+V pastes into fields: Tab moves to the next field, and a new line acts as Field Exit

**Printing:** Ctrl+P prints the current screen to any Windows printer, black on white.

**Kiosk mode:** opens full screen and can't be closed by the user. Ctrl+Alt+Shift+F12 lets IT out, and Windows sign-out still works.

The full key list is in the app under **About**.

## Deploying to many PCs

Install silently with the host preset (GPO, Intune, PDQ or a script):

```
Ham5250-Setup-1.0.0.exe /VERYSILENT /HOST=hamibm.corp.ham.com /PORT=992 /PREFIX=H /KIOSK=0 /LOCK=1
```

This writes `C:\ProgramData\Hamricks5250\settings.json`, the default for every user on the PC.

- `/LOCK=1` stops users changing the connection.
- `/KIOSK=1` makes the PC a dedicated terminal.

See `config/settings.example.json` for every option.

Command line: `Ham5250.exe [--profile "Name"] [--host name] [--port 992] [--notls] [--kiosk]`

## Building

GitHub Actions builds every push on Windows. Pushing a tag publishes a release:

```
git tag v1.0.0
git push origin v1.0.0
```

To build locally on Windows with the .NET 8 SDK:

```
dotnet run --project tests/Hamricks5250.Tests        # engine tests
dotnet run --project src/Hamricks5250.App             # run the app
```

## Layout

| Path | What it is |
|---|---|
| `src/Hamricks5250.Core` | The 5250 engine: telnet/TLS, data stream, screen and fields, keyboard rules, EBCDIC. No Windows dependencies. |
| `src/Hamricks5250.App` | The Windows 11 app (WPF): drawing, keyboard and mouse, tabs, settings, printing, branding |
| `src/Hamricks5250.App/Theme.cs` | Brand colors and styles. **Update the Hamrick's colors here.** |
| `src/Hamricks5250.App/Assets` | `hamricks.ico` (app icon) and `logo.png` (header logo). Replace these with the official artwork. |
| `tests/Hamricks5250.Tests` | Engine tests, including a full session against a simulated IBM i |
| `installer/Ham5250.iss` | Inno Setup installer |

## Testing before rollout

The engine is tested against a simulated IBM i (sign-on, fields, errors, save/restore, telnet negotiation). Check these on the real system:

- [ ] Connect on 992 with TLS. If the certificate is self-signed, put its thumbprint in Settings.
- [ ] The device name shows up on the IBM i (`WRKCFGSTS *DEV H*`)
- [ ] Sign on, main menu, and the warehouse and inventory programs the terminals actually use
- [ ] Numeric and signed fields, F-keys, Page Up/Down on subfiles
- [ ] Sys Req and Attention menus; errors cleared with Reset
- [ ] Any 27×132 screens (turn on "Allow 27 x 132" in Settings)
- [ ] Printing, and any 5250 printer sessions the terminals use today (not yet supported here)

Not supported yet:
- 5250 printer sessions
- GUI windows drawn by the IBM i, which show as plain text with borders
- Double-byte languages

---
Hamrick's name and logo are trademarks of Hamrick's, Inc. Internal use only.

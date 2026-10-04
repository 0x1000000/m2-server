# M2 Server

M2 Server is a Windows app for switching monitor profiles—from your PC or from a browser on another device. It helps you get the right display setup ready for streaming, and get back to your regular monitors when you are done.

## Why I built it

I often stream games with Moonlight or play VR games. For streaming, I use a virtual display so I can choose the resolution and refresh rate I need without having to change my physical monitor setup every time.

The annoying part is getting back. My PC sometimes gets stuck using the virtual display, leaving me without a usable picture on my real monitors. Fixing the display setup is difficult when I cannot see the desktop.

That is the main reason M2 Server has a web interface: I can open it from another device and switch back to a monitor profile that works. It is also handy for preparing the PC before starting a stream.

I also have two Windows installations on separate drives—one for work and one for games. Switching between them remotely, then starting a stream, is much more convenient than going back to the PC. M2 Server can run saved scripts remotely too, which gives me a way to handle that part of my setup.

## What it does

- **Save monitor profiles.** Keep different display setups for your desk, Moonlight streaming, or VR sessions, including which displays are enabled and their resolution and refresh rate.
- **Switch profiles locally.** Use the desktop app or the system tray menu to apply a saved setup.
- **Switch profiles remotely.** Open the web interface from another device to prepare your streaming display or bring your physical monitors back.
- **Run saved scripts.** Use scripts for other actions in your setup, such as restarting into another Windows installation. You provide the scripts that suit your machine.
- **Use remote controls before signing in.** With the optional web service enabled, saved profiles and scripts can run before Windows sign-in.

The desktop app handles configuration. The web interface lets you run the profiles and scripts you have already saved.

## Getting started

M2 Server runs on **Windows 11 x64**.

1. Install M2 Server using the MSI installer and launch it.
2. Open the desktop editor and create profiles for the display setups you use. A good starting point is one for your physical monitors and one for your virtual display.
3. Save your changes, then try switching profiles from the app or tray menu.
4. If you want remote access, enable the web service in Settings, configure access and authentication, and open its address from another device.
5. Add any scripts you want to run, then save them before using them from the tray or web interface.

The web service is optional. You can use M2 Server entirely from the desktop and tray if you only need local profile switching. The tray agent is written in C to keep resource use low, using up to 1 MB of RAM. RAM is expensive these days—no reason to waste it on a tray icon.

## A few things to know

- M2 Server uses an existing virtual display; it does not install a virtual display driver.
- Changes in the editor take effect after you click **Save**.
- If Windows rejects a display profile, the app reports the error. It does not automatically restore the previous layout.
- Keep remote access on a trusted network. Scripts launched through the Windows service run with system privileges, so only save scripts you trust. HTTPS is available with a configured certificate.
- A full uninstall deletes M2 Server's saved data for all local Windows users. Upgrades keep it.

## Building from source

You will need the system .NET 10 SDK, Node.js, Visual Studio C++ build tools, and WiX 7 for the MSI installer.

Run `publish.cmd`, or use the **Publish M2 Server release** configuration in Rider. The release files and installer are written to `out/`.

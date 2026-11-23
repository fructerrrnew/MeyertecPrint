#!/usr/bin/env python3
"""Build desktop installers from a self-contained .NET publish."""

import argparse
import hashlib
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import sys
import tempfile
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
WINDOWS_PROJECT = ROOT / "MeyertecPrint.csproj"
LINUX_PROJECT = ROOT / "Linux" / "MeyertecPrint.Linux.csproj"
NAME = "MEYERTEC Print"
PACKAGE = "meyertecprint"
UPGRADE_CODE = uuid.UUID("f89f85c0-f71b-4d15-ae49-3c0bf406a634")
RUNTIMES = {
    "windows": ("win-x64", WINDOWS_PROJECT, "MeyertecPrint.exe", "coreclr.dll"),
    "linux": ("linux-x64", LINUX_PROJECT, "MeyertecPrint.Linux", "libcoreclr.so"),
}


def installer_versions(version):
    match = re.fullmatch(r"(\d+)\.(\d+)\.(\d+)(?:-([A-Za-z0-9]+(?:[.-][A-Za-z0-9]+)*))?", version)
    if match is None:
        raise ValueError("Use a release version such as 1.2.3 or 1.2.3-preview.1.")
    major, minor, build = map(int, match.groups()[:3])
    if major > 255 or minor > 255 or build > 65535:
        raise ValueError("Product version exceeds the Windows Installer version limits.")
    numeric = f"{major}.{minor}.{build}"
    prerelease = match.group(4)
    return numeric, f"{numeric}~{prerelease}" if prerelease else numeric


def publish(target, version, stage):
    runtime, project, executable, _ = RUNTIMES[target]
    payload = stage / "publish"
    environment = os.environ.copy()
    excludes = environment.get("DefaultItemExcludes", "")
    environment["DefaultItemExcludes"] = f"{excludes};bin/**;obj/**"
    command = [
        "dotnet", "publish", str(project), "-c", "Release", "-r", runtime,
        "--self-contained", "true", "--artifacts-path", str(stage / "artifacts"),
        "-o", str(payload), f"-p:Version={version}",
    ]
    if executable.endswith(".exe"):
        command.append("-p:EnableWindowsTargeting=true")
    subprocess.run(command, cwd=ROOT, env=environment, check=True)
    return payload


def identifier(kind, path):
    digest = hashlib.sha256(path.casefold().encode("utf-8")).hexdigest()
    return f"{kind}_{digest[:24]}"


def component(parent, key):
    return ET.SubElement(parent, "Component", Id=identifier("C", key),
                         Guid=str(uuid.uuid5(UPGRADE_CODE, key.casefold())), Win64="yes")


def windows_installer(payload, stage, output, version, file_version):
    source = stage / "MeyertecPrint.wxs"
    wix = ET.Element("Wix", xmlns="http://schemas.microsoft.com/wix/2006/wi")
    product = ET.SubElement(
        wix, "Product", Id="*", Name=NAME, Language="1033", Version=version,
        Manufacturer="MEYERTEC", UpgradeCode=str(UPGRADE_CODE),
    )
    ET.SubElement(product, "Package", InstallerVersion="500", Compressed="yes", InstallScope="perMachine")
    ET.SubElement(product, "MajorUpgrade", DowngradeErrorMessage="A newer version of MEYERTEC Print is already installed.",
                  AllowSameVersionUpgrades="yes")
    ET.SubElement(product, "Media", Id="1", Cabinet="application.cab", EmbedCab="yes")
    ET.SubElement(product, "Icon", Id="AppIcon", SourceFile=os.path.relpath(ROOT / "IconsUI" / "Meyertec.ico", stage))
    ET.SubElement(product, "Property", Id="ARPPRODUCTICON", Value="AppIcon")
    ET.SubElement(product, "Property", Id="ARPURLINFOABOUT", Value="https://github.com/fructerrrnew/MeyertecPrint")

    target = ET.SubElement(product, "Directory", Id="TARGETDIR", Name="SourceDir")
    programs = ET.SubElement(target, "Directory", Id="ProgramFiles64Folder")
    directories = {Path("."): ET.SubElement(programs, "Directory", Id="INSTALLDIR", Name=NAME)}
    start_menu = ET.SubElement(target, "Directory", Id="ProgramMenuFolder")
    menu_folder = ET.SubElement(start_menu, "Directory", Id="ApplicationProgramsFolder", Name=NAME)

    shortcuts = component(menu_folder, "shortcut/start-menu")
    ET.SubElement(shortcuts, "Shortcut", Id="ApplicationShortcut", Name=NAME, Description=NAME,
                  Target="[INSTALLDIR]MeyertecPrint.exe", WorkingDirectory="INSTALLDIR", Icon="AppIcon")
    ET.SubElement(shortcuts, "RemoveFolder", Id="RemoveApplicationProgramsFolder", On="uninstall")
    ET.SubElement(shortcuts, "RegistryValue", Root="HKLM", Key=r"Software\MEYERTEC\MeyertecPrint",
                  Name="Installed", Type="integer", Value="1", KeyPath="yes")

    feature = ET.SubElement(product, "Feature", Id="Application", Title=NAME, Level="1")
    ET.SubElement(feature, "ComponentRef", Id=shortcuts.attrib["Id"])

    files = sorted(item for item in payload.rglob("*") if item.is_file())
    for item in files:
        relative = item.relative_to(payload)
        for folder in reversed(relative.parents):
            if folder not in directories:
                directories[folder] = ET.SubElement(
                    directories[folder.parent], "Directory",
                    Id=identifier("D", folder.as_posix()), Name=folder.name,
                )
        key = relative.as_posix()
        entry = component(directories[relative.parent], "file/" + key)
        ET.SubElement(entry, "File", Id=identifier("F", key), Name=item.name,
                      Source=os.path.relpath(item, stage), DiskId="1", KeyPath="yes")
        ET.SubElement(feature, "ComponentRef", Id=entry.attrib["Id"])

    ET.indent(wix)
    ET.ElementTree(wix).write(source, encoding="utf-8", xml_declaration=True)

    filename = f"MeyertecPrint-{file_version}-win-x64.msi"
    subprocess.run(["wixl", "--arch", "x64", "-o", filename, source.name], cwd=stage, check=True)
    return deliver(stage / filename, output)


def linux_installer(payload, stage, output, version):
    package = stage / PACKAGE
    install = package / "usr/lib" / PACKAGE
    shutil.copytree(payload, install)
    (install / RUNTIMES["linux"][2]).chmod(0o755)

    desktop_dir = package / "usr/share/applications"
    desktop_dir.mkdir(parents=True)
    shutil.copyfile(ROOT / "packaging" / "meyertecprint.desktop", desktop_dir / f"{PACKAGE}.desktop")

    icons = package / "usr/share/icons/hicolor/512x512/apps"
    icons.mkdir(parents=True)
    shutil.copyfile(ROOT / "IconsUI/Brand.png", icons / f"{PACKAGE}.png")

    binaries = package / "usr/bin"
    binaries.mkdir(parents=True)
    launcher = binaries / PACKAGE
    launcher.write_text('#!/bin/sh\nexec /usr/lib/meyertecprint/MeyertecPrint.Linux "$@"\n', encoding="utf-8")
    launcher.chmod(0o755)

    size = sum(item.stat().st_size for item in package.rglob("*") if item.is_file())
    control = package / "DEBIAN"
    control.mkdir()
    (control / "control").write_text(
        f"Package: {PACKAGE}\n"
        f"Version: {version}\n"
        "Architecture: amd64\n"
        "Section: utils\n"
        "Priority: optional\n"
        "Maintainer: MeyertecPrint project <fructerrrnew@users.noreply.github.com>\n"
        f"Installed-Size: {(size + 1023) // 1024}\n"
        "Depends: libc6 (>= 2.34), libgcc-s1, libstdc++6, zlib1g, libssl3t64 | libssl3,\n"
        " libicu76 | libicu74 | libicu72 | libicu78, libx11-6, libice6, libsm6,\n"
        " libxcursor1, libxrandr2, libfontconfig1, libgl1, libxtst6, libgtk-3-0t64 | libgtk-3-0,\n"
        " fonts-dejavu-core, ca-certificates\n"
        "Homepage: https://github.com/fructerrrnew/MeyertecPrint\n"
        "Description: Design and print MEYERTEC industrial labels\n"
        " Desktop label editor with project files, templates and print preview.\n",
        encoding="utf-8",
    )

    package.chmod(0o755)
    for item in package.rglob("*"):
        mode = item.stat().st_mode
        item.chmod(0o755 if item.is_dir() or mode & (stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH) else 0o644)

    filename = f"{PACKAGE}_{version}_amd64.deb"
    subprocess.run(["dpkg-deb", "--root-owner-group", "--build", str(package), str(stage / filename)], check=True)
    return deliver(stage / filename, output)


def deliver(installer, output):
    destination = output / installer.name
    with tempfile.TemporaryDirectory(prefix=".meyertec-output-", dir=output) as stage:
        final = Path(stage) / installer.name
        shutil.copyfile(installer, final)
        final.chmod(0o644)
        final.replace(destination)
    return destination


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target", choices=RUNTIMES)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--version", help="defaults to the version in MeyertecPrint.csproj")
    parser.add_argument("--from-publish", type=Path, help="package an existing self-contained publish directory")
    args = parser.parse_args()
    try:
        version = args.version or ET.parse(WINDOWS_PROJECT).findtext("./PropertyGroup/Version")
        if not version:
            raise ValueError("Specify the application version with --version.")
        windows_version, linux_version = installer_versions(version)
        output = args.output.resolve()
        output.mkdir(parents=True, exist_ok=True)

        with tempfile.TemporaryDirectory(prefix="meyertec-installer-") as work:
            stage = Path(work)
            payload = args.from_publish.resolve() if args.from_publish else publish(args.target, version, stage)
            _, _, executable, runtime = RUNTIMES[args.target]
            if not (payload / executable).is_file() or not (payload / runtime).is_file():
                raise ValueError(f"{payload} must contain the published {executable} and {runtime}.")

            if args.target == "windows":
                installer = windows_installer(payload, stage, output, windows_version, version)
            else:
                installer = linux_installer(payload, stage, output, linux_version)
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"{error}\n")
    print(installer)


if __name__ == "__main__":
    main()

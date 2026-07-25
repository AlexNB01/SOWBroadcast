# -*- mode: python ; coding: utf-8 -*-
# PyInstaller spec – SOWBroadcast GUI
# Käyttö: pyinstaller SOWBroadcast.spec

block_cipher = None

a = Analysis(
    ['SOWBroadcast.py'],
    pathex=[],
    binaries=[],
    datas=[],
    # server.py importoidaan dynaamisesti nimellä _sb__force_include – varmista paketointi.
    # python-socketio/engineio lataavat kuljetintoteutuksensa dynaamisesti, joten ne on
    # listattava erikseen jotta PyInstaller löytää ne (SOWDraft-live-link, draft_link.py).
    hiddenimports=[
        'server',
        'draft_link',
        'engineio.async_drivers.threading',
        'websocket',
    ],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=['matplotlib', 'numpy', 'pandas', 'tkinter', 'PIL'],
    win_no_prefer_redirects=False,
    win_private_assemblies=False,
    cipher=block_cipher,
    noarchive=False,
)

pyz = PYZ(a.pure, a.zipped_data, cipher=block_cipher)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.zipfiles,
    a.datas,
    [],
    name='SOWBroadcast',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=False,          # GUI-sovellus – ei konsoliikkunaa
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    icon='icon.ico',
)

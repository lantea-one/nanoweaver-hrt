; ==============================================================================
; NanoWeaver HRT: Windows Dual-Scope NSIS Installer
; ------------------------------------------------------------------------------
; WHAT: Modern UI 2 dual-scope installer supporting Current User and System install.
; WHY:  Standard users can install to %LocalAppData%\Programs\NanoWeaverHrt without UAC.
;       Administrators are presented with an interactive installation scope selection,
;       defaulting to Current User, with CLI flag support (/CURRENTUSER, /ALLUSERS).
; ==============================================================================

Unicode true

!define PRODUCT_NAME "NanoWeaver HRT"
!define PRODUCT_PUBLISHER "Lantea Sovereign Research"
!define PRODUCT_WEB_SITE "https://git.lantea.one/fabigail/nanoweaver-hrt"
!define PRODUCT_VERSION "2026.10.29.2"
!define PRODUCT_EXE "NanoWeaverHrt.exe"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\NanoWeaverHrt"

; MultiUser Configuration
!define MULTIUSER_EXECUTIONLEVEL Highest
!define MULTIUSER_MUI
!define MULTIUSER_INSTALLMODE_DEFAULT_CURRENTUSER
!define MULTIUSER_INSTALLMODE_COMMANDLINE
!define MULTIUSER_INSTALLMODE_INSTDIR "NanoWeaverHrt"
!define MULTIUSER_INSTALLMODE_INSTDIR_REGISTRY_KEY "${PRODUCT_UNINST_KEY}"
!define MULTIUSER_INSTALLMODE_INSTDIR_REGISTRY_VALUENAME "InstallLocation"

!include "MultiUser.nsh"
!include "MUI2.nsh"
!include "LogicLib.nsh"

Name "${PRODUCT_NAME} ${PRODUCT_VERSION}"
OutFile "..\..\artifacts\packages\windows\nanoweaver-hrt-desktop-installer.exe"

; Interface Settings
!define MUI_ABORTWARNING
!define MUI_ICON "nanoweaver-hrt-desktop.ico"
!define MUI_UNICON "nanoweaver-hrt-desktop.ico"

; Installer Pages
!insertmacro MULTIUSER_PAGE_INSTALLMODE
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

; Uninstaller Pages
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

; Language
!insertmacro MUI_LANGUAGE "English"

Function .onInit
    !insertmacro MULTIUSER_INIT
FunctionEnd

Function un.onInit
    !insertmacro MULTIUSER_UNINIT
FunctionEnd

Section "NanoWeaver HRT Core" SecCore
    SectionIn RO
    SetOutPath "$INSTDIR"
    File /r "..\..\artifacts\staging\win-x64\*.*"

    ; Create Shortcuts based on MultiUser context ($SMPROGRAMS resolves to per-user or common)
    CreateDirectory "$SMPROGRAMS\NanoWeaver HRT"
    CreateShortcut "$SMPROGRAMS\NanoWeaver HRT\NanoWeaver HRT.lnk" "$INSTDIR\${PRODUCT_EXE}" "" "$INSTDIR\${PRODUCT_EXE}" 0
    CreateShortcut "$SMPROGRAMS\NanoWeaver HRT\Uninstall.lnk" "$INSTDIR\Uninstall.exe" "" "$INSTDIR\Uninstall.exe" 0

    ; Write Uninstaller
    WriteUninstaller "$INSTDIR\Uninstall.exe"

    ; Write Registry Entries in SHCTX (HKCU or HKLM depending on chosen mode)
    WriteRegStr SHCTX "${PRODUCT_UNINST_KEY}" "DisplayName" "${PRODUCT_NAME}"
    WriteRegStr SHCTX "${PRODUCT_UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegStr SHCTX "${PRODUCT_UNINST_KEY}" "DisplayIcon" "$INSTDIR\${PRODUCT_EXE}"
    WriteRegStr SHCTX "${PRODUCT_UNINST_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
    WriteRegStr SHCTX "${PRODUCT_UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
    WriteRegStr SHCTX "${PRODUCT_UNINST_KEY}" "URLInfoAbout" "${PRODUCT_WEB_SITE}"
    WriteRegStr SHCTX "${PRODUCT_UNINST_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegDWORD SHCTX "${PRODUCT_UNINST_KEY}" "NoModify" 1
    WriteRegDWORD SHCTX "${PRODUCT_UNINST_KEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
    Delete "$SMPROGRAMS\NanoWeaver HRT\NanoWeaver HRT.lnk"
    Delete "$SMPROGRAMS\NanoWeaver HRT\Uninstall.lnk"
    RMDir "$SMPROGRAMS\NanoWeaver HRT"

    RMDir /r "$INSTDIR"

    DeleteRegKey SHCTX "${PRODUCT_UNINST_KEY}"
SectionEnd

#define UNICODE
#define _UNICODE
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <sddl.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>

#define WM_TRAY (WM_APP + 1)
#define WM_NOTIFICATION (WM_APP + 2)
#define NOTIFICATION_PIPE_PREFIX L"\\\\.\\pipe\\M2Server.Tray.Notify."
#define ARG_UI L"--ui"
#define ARG_APPLY_PROFILE L"--apply-profile"
#define ARG_RUN_SCRIPT L"--run-script"
#define DATA_FILE_RELATIVE_PATH L"\\M2 Server\\data.json"
#define SERVICE_REFERENCE_RELATIVE_PATH L"\\M2 Server\\service-data-directory.txt"
#define NOTIFICATION_PIPE_CLIENT_ACCESS (FILE_WRITE_DATA | FILE_READ_ATTRIBUTES | SYNCHRONIZE)
#define MAX_ITEMS 256
typedef struct { DWORD warning; wchar_t message[256]; } Notification;
typedef struct { wchar_t id[64]; wchar_t name[256]; } Item;
static Item profiles[MAX_ITEMS], scripts[MAX_ITEMS];
static int profile_count, script_count;
static NOTIFYICONDATAW icon;
static UINT taskbar_created;
static HWND menu_owner;

static void *notification_pipe_descriptor(void) {
    HANDLE token = NULL;
    TOKEN_GROUPS *groups = NULL;
    LPWSTR logon_sid = NULL;
    void *descriptor = NULL;
    DWORD size = 0;
    wchar_t acl[128];

    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) goto done;
    GetTokenInformation(token, TokenLogonSid, NULL, 0, &size);
    if (GetLastError() != ERROR_INSUFFICIENT_BUFFER || !size) goto done;
    groups = (TOKEN_GROUPS *)malloc(size);
    if (!groups || !GetTokenInformation(token, TokenLogonSid, groups, size, &size) ||
        groups->GroupCount != 1 ||
        !ConvertSidToStringSidW(groups->Groups[0].Sid, &logon_sid)) goto done;
    if (swprintf_s(acl, _countof(acl), L"D:P(A;;GA;;;SY)(A;;0x%08lX;;;%ls)",
            (unsigned long)NOTIFICATION_PIPE_CLIENT_ACCESS, logon_sid) < 0) goto done;
    if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(acl, SDDL_REVISION_1, &descriptor, NULL))
        descriptor = NULL;

done:
    LocalFree(logon_sid);
    free(groups);
    if (token) CloseHandle(token);
    return descriptor;
}

static DWORD WINAPI notification_pipe(void *parameter) {
    HWND window = (HWND)parameter;
    DWORD session;
    void *descriptor;
    wchar_t name[128];
    SECURITY_ATTRIBUTES attributes = { sizeof(attributes) };
    if (!ProcessIdToSessionId(GetCurrentProcessId(), &session)) return 0;
    descriptor = notification_pipe_descriptor();
    if (!descriptor) return 0;
    attributes.lpSecurityDescriptor = descriptor;
    swprintf_s(name, _countof(name), L"%ls%lu", NOTIFICATION_PIPE_PREFIX, session);
    for (;;) {
        Notification *notification;
        DWORD read = 0;
        OVERLAPPED operation = {0};
        BOOL connected;
        HANDLE pipe = CreateNamedPipeW(name, PIPE_ACCESS_INBOUND | FILE_FLAG_FIRST_PIPE_INSTANCE | FILE_FLAG_OVERLAPPED,
            PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
            1, sizeof(Notification), sizeof(Notification), 0, &attributes);
        if (pipe == INVALID_HANDLE_VALUE) break;
        operation.hEvent = CreateEventW(NULL, TRUE, FALSE, NULL);
        if (!operation.hEvent) { CloseHandle(pipe); break; }
        connected = ConnectNamedPipe(pipe, &operation);
        if (!connected && GetLastError() == ERROR_PIPE_CONNECTED) connected = TRUE;
        else if (!connected && GetLastError() == ERROR_IO_PENDING &&
                 WaitForSingleObject(operation.hEvent, INFINITE) == WAIT_OBJECT_0)
            connected = GetOverlappedResult(pipe, &operation, &read, FALSE);
        if (connected) {
            notification = (Notification *)calloc(1, sizeof(Notification));
            if (notification) {
                BOOL received;
                HANDLE event = operation.hEvent;
                ZeroMemory(&operation, sizeof(operation));
                operation.hEvent = event;
                ResetEvent(event);
                received = ReadFile(pipe, notification, sizeof(*notification), &read, &operation);
                if (!received && GetLastError() == ERROR_IO_PENDING) {
                    if (WaitForSingleObject(operation.hEvent, 2000) == WAIT_OBJECT_0)
                        received = GetOverlappedResult(pipe, &operation, &read, FALSE);
                    else {
                        CancelIoEx(pipe, &operation);
                        WaitForSingleObject(operation.hEvent, INFINITE);
                    }
                }
                if (received && read == sizeof(*notification) && notification->warning <= 1 &&
                    wmemchr(notification->message, L'\0', _countof(notification->message)) &&
                    PostMessageW(window, WM_NOTIFICATION, 0, (LPARAM)notification)) {
                    notification = NULL;
                }
                free(notification);
            }
        }
        DisconnectNamedPipe(pipe);
        CloseHandle(pipe);
        CloseHandle(operation.hEvent);
    }
    LocalFree(descriptor);
    return 0;
}

static const char *space(const char *p) {
    while (*p == ' ' || *p == '\r' || *p == '\n' || *p == '\t') ++p;
    return p;
}
static const char *end_string(const char *p) {
    for (; *p; ++p) {
        if (*p == '\\' && p[1]) { ++p; continue; }
        if (*p == '"') return p;
    }
    return p;
}
static void decode(const char *begin, wchar_t *result, int capacity) {
    const char *end = end_string(begin);
    int bytes = (int)(end - begin), count, i, out = 0;
    wchar_t *raw = (wchar_t *)calloc((size_t)bytes + 1, sizeof(wchar_t));
    if (!raw) return;
    count = MultiByteToWideChar(CP_UTF8, 0, begin, bytes, raw, bytes);
    for (i = 0; i < count && out < capacity - 1; ++i) {
        if (raw[i] == L'\\' && i + 1 < count) {
            wchar_t next = raw[++i];
            if (next == L'u' && i + 4 < count) {
                unsigned value = 0;
                int j, valid = 1;
                for (j = 1; j <= 4; ++j) {
                    wchar_t c = raw[i + j];
                    value <<= 4;
                    if (c >= L'0' && c <= L'9') value += c - L'0';
                    else if (c >= L'a' && c <= L'f') value += c - L'a' + 10;
                    else if (c >= L'A' && c <= L'F') value += c - L'A' + 10;
                    else valid = 0;
                }
                if (valid) { result[out++] = (wchar_t)value; i += 4; continue; }
            }
            result[out++] = next == L'n' ? L' ' : next;
        } else result[out++] = raw[i];
    }
    result[out] = 0;
    free(raw);
}
static int field(const char *begin, const char *end, const char *key, wchar_t *out, int cap) {
    char pattern[64];
    const char *p;
    snprintf(pattern, sizeof(pattern), "\"%s\"", key);
    p = strstr(begin, pattern);
    if (!p || p >= end) return 0;
    p = space(p + strlen(pattern));
    if (*p++ != ':') return 0;
    p = space(p);
    if (*p++ != '"') return 0;
    decode(p, out, cap);
    return 1;
}
static const char *end_object(const char *p) {
    int depth = 0, quoted = 0;
    for (; *p; ++p) {
        if (quoted) {
            if (*p == '\\' && p[1]) { ++p; continue; }
            if (*p == '"') quoted = 0;
        } else {
            if (*p == '"') quoted = 1;
            else if (*p == '{') ++depth;
            else if (*p == '}' && --depth == 0) return p + 1;
        }
    }
    return p;
}
static void parse_items(const char *json, const char *key, Item *out, int *count) {
    char pattern[64];
    const char *p;
    *count = 0;
    snprintf(pattern, sizeof(pattern), "\"%s\"", key);
    p = strstr(json, pattern);
    if (!p || !(p = strchr(p + strlen(pattern), '['))) return;
    ++p;
    while (*p && *count < MAX_ITEMS) {
        const char *end;
        p = space(p);
        if (*p == ']') break;
        if (*p == ',') { ++p; continue; }
        if (*p != '{') break;
        end = end_object(p);
        if (field(p, end, "id", out[*count].id, 64) &&
            field(p, end, "name", out[*count].name, 256)) ++*count;
        p = end;
    }
}
static void load_menu(void) {
    wchar_t root[MAX_PATH], path[MAX_PATH];
    HANDLE file;
    DWORD size, read;
    char *json;
    profile_count = script_count = 0;
    if (!GetEnvironmentVariableW(L"LOCALAPPDATA", root, MAX_PATH)) return;
    swprintf_s(path, MAX_PATH, L"%s%s", root, DATA_FILE_RELATIVE_PATH);
    if (GetFileAttributesW(path) == INVALID_FILE_ATTRIBUTES) {
        wchar_t reference[MAX_PATH];
        if (!GetEnvironmentVariableW(L"ProgramData", root, MAX_PATH)) return;
        swprintf_s(reference, MAX_PATH, L"%s%s", root, SERVICE_REFERENCE_RELATIVE_PATH);
        if (GetFileAttributesW(reference) != INVALID_FILE_ATTRIBUTES) return;
        swprintf_s(path, MAX_PATH, L"%s%s", root, DATA_FILE_RELATIVE_PATH);
    }
    file = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                       NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) return;
    size = GetFileSize(file, NULL);
    if (size == INVALID_FILE_SIZE || size > 16 * 1024 * 1024) { CloseHandle(file); return; }
    json = (char *)malloc((size_t)size + 1);
    if (json && ReadFile(file, json, size, &read, NULL)) {
        json[read] = 0;
        parse_items(json, "presets", profiles, &profile_count);
        parse_items(json, "scripts", scripts, &script_count);
    }
    free(json);
    CloseHandle(file);
}
static int administrator(void) {
    BYTE sid[SECURITY_MAX_SID_SIZE];
    DWORD size = sizeof(sid), needed = 0;
    BOOL member = FALSE;
    HANDLE token = NULL;
    TOKEN_LINKED_TOKEN linked;
    if (!CreateWellKnownSid(WinBuiltinAdministratorsSid, NULL, sid, &size)) return 0;
    CheckTokenMembership(NULL, sid, &member);
    if (member) return 1;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return 0;
    if (GetTokenInformation(token, TokenLinkedToken, &linked, sizeof(linked), &needed)) {
        CheckTokenMembership(linked.LinkedToken, sid, &member);
        CloseHandle(linked.LinkedToken);
    }
    CloseHandle(token);
    return member != FALSE;
}
static void launch(const wchar_t *action, const wchar_t *id) {
    wchar_t path[MAX_PATH], *slash, command[MAX_PATH + 128];
    STARTUPINFOW startup = { sizeof(startup) };
    PROCESS_INFORMATION process;
    GetModuleFileNameW(NULL, path, MAX_PATH);
    slash = wcsrchr(path, L'\\');
    if (!slash) return;
    wcscpy_s(slash + 1, MAX_PATH - (size_t)(slash + 1 - path), L"M2Server.App.exe");
    swprintf_s(command, MAX_PATH + 128, L"\"%s\" %s %s", path, action, id ? id : L"");
    if (CreateProcessW(path, command, NULL, NULL, FALSE, 0, NULL, NULL, &startup, &process)) {
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
    }
}
static void show_menu(HWND window) {
    DPI_AWARENESS_CONTEXT previous_dpi = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    HMENU menu = CreatePopupMenu();
    HWND owner;
    POINT point;
    int i, choice = 0;
    if (!menu) goto done;
    if (!menu_owner) {
        menu_owner = CreateWindowExW(WS_EX_TOOLWINDOW, L"STATIC", L"", WS_POPUP,
                                     0, 0, 0, 0, NULL, NULL, NULL, NULL);
    }
    owner = menu_owner ? menu_owner : window;
    load_menu();
    AppendMenuW(menu, MF_STRING, 1, L"Open M2 Server");
    AppendMenuW(menu, MF_SEPARATOR, 0, NULL);
    for (i = 0; i < profile_count; ++i) AppendMenuW(menu, MF_STRING, 1000 + i, profiles[i].name);
    if (administrator() && script_count) {
        AppendMenuW(menu, MF_SEPARATOR, 0, NULL);
        for (i = 0; i < script_count; ++i) AppendMenuW(menu, MF_STRING, 2000 + i, scripts[i].name);
    }
    AppendMenuW(menu, MF_SEPARATOR, 0, NULL);
    AppendMenuW(menu, MF_STRING, 2, L"Exit");
    GetCursorPos(&point);
    if (owner != window) ShowWindow(owner, SW_SHOW);
    SetForegroundWindow(owner);
    choice = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, point.x, point.y, 0, owner, NULL);
    PostMessageW(owner, WM_NULL, 0, 0);
    if (owner != window) ShowWindow(owner, SW_HIDE);
    DestroyMenu(menu);
done:
    if (previous_dpi) SetThreadDpiAwarenessContext(previous_dpi);
    if (choice == 1) launch(ARG_UI, NULL);
    else if (choice == 2) DestroyWindow(window);
    else if (choice >= 1000 && choice < 1000 + profile_count)
        launch(ARG_APPLY_PROFILE, profiles[choice - 1000].id);
    else if (choice >= 2000 && choice < 2000 + script_count)
        launch(ARG_RUN_SCRIPT, scripts[choice - 2000].id);
}
static void add_icon(HWND window) {
    ZeroMemory(&icon, sizeof(icon));
    icon.cbSize = sizeof(icon);
    icon.hWnd = window;
    icon.uID = 1;
    icon.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
    icon.uCallbackMessage = WM_TRAY;
    icon.hIcon = LoadIconW(GetModuleHandleW(NULL), MAKEINTRESOURCEW(1));
    wcscpy_s(icon.szTip, _countof(icon.szTip), L"M2 Server");
    Shell_NotifyIconW(NIM_ADD, &icon);
}
static LRESULT CALLBACK window_proc(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
    if (message == taskbar_created) { add_icon(window); return 0; }
    if (message == WM_NOTIFICATION) {
        Notification *notification = (Notification *)lparam;
        NOTIFYICONDATAW balloon = icon;
        balloon.uFlags = NIF_INFO;
        wcscpy_s(balloon.szInfoTitle, _countof(balloon.szInfoTitle), L"M2 Server");
        wcscpy_s(balloon.szInfo, _countof(balloon.szInfo), notification->message);
        balloon.dwInfoFlags = notification->warning ? NIIF_WARNING : NIIF_INFO;
        Shell_NotifyIconW(NIM_MODIFY, &balloon);
        free(notification);
        return 0;
    }
    if (message == WM_TRAY) {
        if (lparam == WM_LBUTTONUP) launch(ARG_UI, NULL);
        if (lparam == WM_RBUTTONUP || lparam == WM_CONTEXTMENU) show_menu(window);
        return 0;
    }
    if (message == WM_DESTROY) {
        Shell_NotifyIconW(NIM_DELETE, &icon);
        if (menu_owner) DestroyWindow(menu_owner);
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(window, message, wparam, lparam);
}
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, PWSTR command, int show) {
    (void)previous;
    (void)command;
    (void)show;
    WNDCLASSW cls = {0};
    HWND window;
    MSG message;
    HANDLE pipe_thread;
    HANDLE mutex = CreateMutexW(NULL, TRUE, L"Local\\M2Server.Tray.SingleInstance");
    if (!mutex || GetLastError() == ERROR_ALREADY_EXISTS) return 0;
    taskbar_created = RegisterWindowMessageW(L"TaskbarCreated");
    cls.lpfnWndProc = window_proc;
    cls.hInstance = instance;
    cls.lpszClassName = L"M2ServerTrayWindow";
    RegisterClassW(&cls);
    window = CreateWindowExW(0, cls.lpszClassName, L"M2 Server Tray", 0,
                             0, 0, 0, 0, NULL, NULL, instance, NULL);
    if (!window) return 1;
    add_icon(window);
    pipe_thread = CreateThread(NULL, 0, notification_pipe, window, 0, NULL);
    if (pipe_thread) CloseHandle(pipe_thread);
    while (GetMessageW(&message, NULL, 0, 0) > 0) {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }
    CloseHandle(mutex);
    return 0;
}

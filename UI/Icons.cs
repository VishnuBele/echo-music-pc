using Avalonia.Media;

namespace EchoMusic.UI;

public static class Icons
{
    public static StreamGeometry Parse(string svg) => StreamGeometry.Parse(svg);

    public static readonly StreamGeometry Home = Parse("M10 20v-6h4v6h5v-8h3L12 3 2 12h3v8z");
    public static readonly StreamGeometry Explore = Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm2.19 12.19L6 18l3.81-8.19L18 6l-3.81 8.19z");
    public static readonly StreamGeometry Flow = Parse("M13 2L3 14h9l-1 8 10-12h-9l1-8z");
    public static readonly StreamGeometry Library = Parse("M4 6H2v14c0 1.1.9 2 2 2h14v-2H4V6zm16-4H8c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm-2 9h-3v3.5c0 1.38-1.12 2.5-2.5 2.5S10 15.88 10 14.5 11.12 12 12.5 12c.57 0 1.08.19 1.5.51V6h4v5z");
    public static readonly StreamGeometry HeartOutline = Parse("M16.5 3c-1.74 0-3.41.81-4.5 2.09C10.91 3.81 9.24 3 7.5 3 4.42 3 2 5.42 2 8.5c0 3.78 3.4 6.86 8.55 11.54L12 21.35l1.45-1.32C18.6 15.36 22 12.28 22 8.5 22 5.42 19.58 3 16.5 3zm-4.4 15.55l-.1.1-.1-.1C7.14 14.24 4 11.39 4 8.5 4 6.5 5.5 5 7.5 5c1.54 0 3.04.99 3.57 2.36h1.87C13.46 5.99 14.96 5 16.5 5c2 0 3.5 1.5 3.5 3.5 0 2.89-3.14 5.74-7.9 10.05z");
    public static readonly StreamGeometry HeartFilled = Parse("M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z");
    public static readonly StreamGeometry Download = Parse("M5 20h14v-2H5v2zM19 9h-4V3H9v6H5l7 7 7-7z");
    public static readonly StreamGeometry CheckCircle = Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z");
    public static readonly StreamGeometry Folder = Parse("M10 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z");
    public static readonly StreamGeometry Spotify = Parse("M12 2C6.5 2 2 6.5 2 12s4.5 10 10 10 10-4.5 10-10S17.5 2 12 2zm4.59 14.42c-.18.3-.56.39-.86.21-2.35-1.44-5.31-1.76-8.8-.96-.34.08-.67-.13-.75-.47-.08-.34.13-.67.47-.75 3.82-.87 7.09-.5 9.73 1.11.3.18.39.56.21.86zm1.22-2.72c-.23.37-.71.49-1.08.26-2.69-1.65-6.79-2.13-9.97-1.17-.42.13-.86-.11-.99-.52-.13-.42.11-.86.52-.99 3.63-1.1 8.15-.57 11.26 1.34.37.23.49.71.26 1.08zm.11-2.83C14.7 8.96 9.38 8.78 6.3 9.72c-.49.15-1.01-.13-1.16-.62-.15-.49.13-1.01.62-1.16 3.54-1.08 9.42-.87 13.14 1.34.45.26.6.84.33 1.29-.26.44-.84.59-1.31.3z");
    public static readonly StreamGeometry Stats = Parse("M5 9.2h3V19H5zM10.6 5h2.8v14h-2.8zm5.6 8H19v6h-2.8z");
    public static readonly StreamGeometry Equalizer = Parse("M7 18h2V6H7v12zm4 4h2V2h-2v20zm-8-8h2v-4H3v4zm12 4h2V6h-2v12zm4-8v4h2v-4h-2z");
    public static readonly StreamGeometry Settings = Parse("M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z");
    public static readonly StreamGeometry Search = Parse("M15.5 14h-.79l-.28-.27C15.41 12.59 16 11.11 16 9.5 16 5.91 13.09 3 9.5 3S3 5.91 3 9.5 5.91 16 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z");
    public static readonly StreamGeometry Info = Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z");
    public static readonly StreamGeometry Warning = Parse("M12 2L22.5 21H1.5L12 2z");
    public static readonly StreamGeometry WarningMark = Parse("M11 7h2v8h-2zM11 17h2v2h-2z");
    public static readonly StreamGeometry ShieldCheck = Parse("M12 2 3.5 5v6.4c0 5.1 3.4 9.2 8.5 10.6 5.1-1.4 8.5-5.5 8.5-10.6V5L12 2zm0 2.2 6.5 2.3v4.9c0 3.9-2.5 7.2-6.5 8.5-4-1.3-6.5-4.6-6.5-8.5V6.5L12 4.2zm-1.1 10.6-2.3-2.3-1.4 1.4 3.7 3.7 6.1-6.1-1.4-1.4-4.7 4.7z");
    public static readonly StreamGeometry Waveform = Parse("M3 10h2v4H3v-4zm4-4h2v12H7V6zm4 2h2v8h-2V8zm4-5h2v18h-2V3zm4 7h2v4h-2v-4z");
    public static readonly StreamGeometry FastForward = Parse("M4 5v14l9-7-9-7zm10 0v14l9-7-9-7z");
    public static readonly StreamGeometry Play = Parse("M6.5 5v14l11-7z");
    public static readonly StreamGeometry Pause = Parse("M6 19h4V5H6v14zm8-14v14h4V5h-4z");
    public static readonly StreamGeometry Next = Parse("M6 18l8.5-6L6 6v12zM16 6v12h2V6h-2z");
    public static readonly StreamGeometry Prev = Parse("M6 6h2v12H6zm3.5 6l8.5 6V6z");
    public static readonly StreamGeometry Shuffle = Parse("M10.59 9.17L5.41 4 4 5.41l5.17 5.17 1.42-1.41zM14.5 4l2.04 2.04L4 18.59 5.41 20 17.96 7.46 20 9.5V4h-5.5zm.33 9.41l-1.41 1.41 3.13 3.13L14.5 20H20v-5.5l-2.04 2.04-3.13-3.13z");
    public static readonly StreamGeometry Repeat = Parse("M7 7h10v3l4-4-4-4v3H5v6h2V7zm10 10H7v-3l-4 4 4 4v-3h12v-6h-2v4z");
    public static readonly StreamGeometry RepeatOne = Parse("M7 7h10v3l4-4-4-4v3H5v6h2V7zm10 10H7v-3l-4 4 4 4v-3h12v-6h-2v4zm-4-2V9h-1l-2 1v1h1.5v4H13z");
    public static readonly StreamGeometry Lyrics = Parse("M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z");
    public static readonly StreamGeometry Queue = Parse("M15 6H3v2h12V6zm0 4H3v2h12v-2zM3 16h8v-2H3v2zM17 6v8.18c-.31-.11-.65-.18-1-.18-1.66 0-3 1.34-3 3s1.34 3 3 3 3-1.34 3-3V8h3V6h-5z");
    public static readonly StreamGeometry Dna = Parse("M4 2h2v2c0 1.44.68 2.61 1.88 3.78L12 11.6l4.12-3.82C17.32 6.61 18 5.44 18 4V2h2v2c0 2.12-.98 3.84-2.55 5.36L13.5 13l3.95 3.64C19.02 18.16 20 19.88 20 22h-2v-2c0-1.44-.68-2.61-1.88-3.78L12 12.4l-4.12 3.82C6.68 17.39 6 18.56 6 20v2H4v-2c0-2.12.98-3.84 2.55-5.36L10.5 13 6.55 9.36C4.98 7.84 4 6.12 4 4V2z");
    public static readonly StreamGeometry VolumeHigh = Parse("M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02zM14 3.23v2.06c2.89.86 5 3.54 5 6.71s-2.11 5.85-5 6.71v2.06c4.01-.91 7-4.49 7-8.77s-2.99-7.86-7-8.77z");
    public static readonly StreamGeometry VolumeMute = Parse("M16.5 12c0-1.77-1.02-3.29-2.5-4.03v2.21l2.45 2.45c.03-.2.05-.41.05-.63zm2.5 0c0 .94-.2 1.82-.54 2.64l1.51 1.51C20.63 14.91 21 13.5 21 12c0-4.28-2.99-7.86-7-8.77v2.06c2.89.86 5 3.54 5 6.71zM4.27 3L3 4.27 7.73 9H3v6h4l5 5v-6.73l4.25 4.25c-.67.52-1.42.93-2.25 1.18v2.06c1.38-.31 2.63-.95 3.69-1.81L19.73 21 21 19.73l-9-9L4.27 3zM12 4L9.91 6.09 12 8.18V4z");
    public static readonly StreamGeometry Expand = Parse("M7 14H5v5h5v-2H7v-3zm-2-4h2V7h3V5H5v5zm12 7h-3v2h5v-5h-2v3zM14 5v2h3v3h2V5h-5z");
    public static readonly StreamGeometry Mini = Parse("M19 11h-8v6h8v-6zm4 8V4.98C23 3.88 22.1 3 21 3H3c-1.1 0-2 .88-2 1.98V19c0 1.1.9 2 2 2h18c1.1 0 2-.9 2-2zm-2 .02H3V4.97h18v14.05z");
    public static readonly StreamGeometry Plus = Parse("M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z");
    public static readonly StreamGeometry Trash = Parse("M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z");
    public static readonly StreamGeometry Share = Parse("M18 16.08c-.76 0-1.44.3-1.96.77L8.91 12.7c.05-.23.09-.46.09-.7s-.04-.47-.09-.7l7.05-4.11c.54.5 1.25.81 2.04.81 1.66 0 3-1.34 3-3s-1.34-3-3-3-3 1.34-3 3c0 .24.04.47.09.7L8.04 9.81C7.5 9.31 6.79 9 6 9c-1.66 0-3 1.34-3 3s1.34 3 3 3c.79 0 1.5-.31 2.04-.81l7.12 4.16c-.05.21-.08.43-.08.65 0 1.61 1.31 2.92 2.92 2.92 1.61 0 2.92-1.31 2.92-2.92s-1.31-2.92-2.92-2.92z");
    public static readonly StreamGeometry ChevronLeft = Parse("M15.41 7.41L14 6l-6 6 6 6 1.41-1.41L10.83 12z");
    public static readonly StreamGeometry ChevronRight = Parse("M10 6L8.59 7.41 13.17 12l-4.58 4.59L10 18l6-6z");
    public static readonly StreamGeometry ChevronDown = Parse("M7.41 8.59 12 13.17l4.59-4.58L18 10l-6 6-6-6z");
    public static readonly StreamGeometry Close = Parse("M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z");
    public static readonly StreamGeometry Minimize = Parse("M6 19h12v2H6z");
    public static readonly StreamGeometry Maximize = Parse("M4 4h16v16H4V4zm2 2v12h12V6H6z");
    public static readonly StreamGeometry Radio = Parse("M3.24 6.15C2.51 6.43 2 7.17 2 8v12c0 1.1.89 2 2 2h16c1.11 0 2-.9 2-2V8c0-1.11-.89-2-2-2H8.3l8.26-3.34L15.88 1 3.24 6.15zM7 20c-1.66 0-3-1.34-3-3s1.34-3 3-3 3 1.34 3 3-1.34 3-3 3zm13-8h-2v-2h-2v2H4V8h16v4z");
    public static readonly StreamGeometry Translate = Parse("M12.87 15.07l-2.54-2.51.03-.03c1.74-1.94 2.98-4.17 3.71-6.53H17V4h-7V2H8v2H1v1.99h11.17C11.5 7.92 10.44 9.75 9 11.35 8.07 10.32 7.3 9.19 6.69 8h-2c.73 1.63 1.73 3.17 2.98 4.56l-5.09 5.02L4 19l5-5 3.11 3.11.76-2.04zM18.5 10h-2L12 22h2l1.12-3h4.75L21 22h2l-4.5-12zm-2.62 7l1.62-4.33L19.12 17h-3.24z");
    public static readonly StreamGeometry Sleep = Parse("M9 2c-1.05 0-2.05.16-3 .46 4.06 1.27 7 5.06 7 9.54 0 4.48-2.94 8.27-7 9.54.95.3 1.95.46 3 .46 5.52 0 10-4.48 10-10S14.52 2 9 2z");
    public static readonly StreamGeometry MoonSoft = Parse("M20.2 15.7A8.7 8.7 0 0 1 8.3 3.8a8.7 8.7 0 1 0 11.9 11.9Z");
    public static readonly StreamGeometry FastForwardSoft = Parse("M4.8 5.7c0-1.1 1.25-1.7 2.12-1L14 10.2c.68.52.68 1.08 0 1.6l-7.08 5.5c-.87.67-2.12.05-2.12-1.02V5.7Zm9.4 0c0-1.1 1.25-1.7 2.12-1l7.08 5.5c.68.52.68 1.08 0 1.6l-7.08 5.5c-.87.67-2.12.05-2.12-1.02V5.7Z");
    public static readonly StreamGeometry Refresh = Parse("M17.65 6.35A7.95 7.95 0 0 0 12 4V1L7 6l5 5V7a5 5 0 1 1-4.9 6H5.08A7 7 0 1 0 17.65 6.35z");
    public static readonly StreamGeometry Leaf = Parse("M20.5 3.5C11 3.5 5.4 6.2 4.1 11c-.9 3.2 1.1 5.8 4.3 5.8 4.5 0 7.7-4.8 8.8-9.7-2.2 4.6-5.2 7.1-9.3 7.3 2.8-1.4 5.7-4.4 7.2-7.3-1.4 5.5-4.3 9.2-8.5 11.4l1 1.7c5.9-3.2 9.5-8.4 11-16.7l.4-2z");
    public static readonly StreamGeometry User = Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 3c1.66 0 3 1.34 3 3s-1.34 3-3 3-3-1.34-3-3 1.34-3 3-3zm0 14.2c-2.5 0-4.71-1.28-6-3.22.03-1.99 4-3.08 6-3.08 1.99 0 5.97 1.09 6 3.08-1.29 1.94-3.5 3.22-6 3.22z");
    public static readonly StreamGeometry More = Parse("M12 8c1.1 0 2-.9 2-2s-.9-2-2-2-2 .9-2 2 .9 2 2 2zm0 2c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2zm0 6c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2z");
    public static readonly StreamGeometry MoreHorizontal = Parse("M6 10c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2zm6 0c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2zm6 0c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2z");
    public static readonly StreamGeometry Edit = Parse("M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04a1.003 1.003 0 0 0 0-1.42l-2.34-2.34a1.003 1.003 0 0 0-1.42 0l-1.83 1.83 3.75 3.75 1.84-1.82z");
    public static readonly StreamGeometry Dots = Parse("M6 10c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2zm12 0c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2zm-6 0c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2z");
}

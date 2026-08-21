using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace N_m3u8DL_RE_GUI.Core.Abyss
{
    public class AbyssDatas
    {
        [JsonPropertyName("slug")]
        public string Slug { get; set; }

        [JsonPropertyName("md5_id")]
        public int Md5Id { get; set; }

        [JsonPropertyName("user_id")]
        public int UserId { get; set; }

        [JsonPropertyName("media")]
        public string Media { get; set; }
    }

    public class AbyssSource
    {
        [JsonPropertyName("label")]
        public string Label { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("res_id")]
        public int ResId { get; set; }

        [JsonPropertyName("sub")]
        public string Subdomain { get; set; }

        [JsonPropertyName("codec")]
        public string Codec { get; set; }

        [JsonPropertyName("path")]
        public string Path { get; set; }

        [JsonPropertyName("status")]
        public bool Status { get; set; }

        [JsonPropertyName("partSize")]
        public int? PartSize { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }
    }

    public class AbyssFirstData
    {
        [JsonPropertyName("res_id")]
        public int ResId { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("codec")]
        public string Codec { get; set; }

        [JsonPropertyName("partSize")]
        public int? PartSize { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }
    }

    public class AbyssMp4
    {
        [JsonPropertyName("domains")]
        public List<string> Domains { get; set; } = new List<string>();

        [JsonPropertyName("sources")]
        public List<AbyssSource> Sources { get; set; } = new List<AbyssSource>();

        [JsonPropertyName("fristDatas")]
        public List<AbyssFirstData> FirstDatas { get; set; } = new List<AbyssFirstData>();

        [JsonIgnore]
        public string Slug { get; set; }

        [JsonIgnore]
        public int Md5Id { get; set; }
    }

    public class AbyssVideoPayload
    {
        [JsonPropertyName("mp4")]
        public AbyssMp4 Mp4 { get; set; }
    }

    public class AbyssDownloadProgress
    {
        public int DownloadedChunks { get; set; }
        public int TotalChunks { get; set; }
        public long DownloadedBytes { get; set; }
        public long TotalBytes { get; set; }
        public double SpeedBytesPerSec { get; set; }
        public TimeSpan? Eta { get; set; }
        public double Percentage => TotalBytes > 0 ? Math.Min(100.0, DownloadedBytes * 100.0 / TotalBytes) : 0;

        public string FormatN_m3u8DL_RE_Line()
        {
            double speedMBs = SpeedBytesPerSec / (1024.0 * 1024.0);
            double mbDownloaded = DownloadedBytes / (1024.0 * 1024.0);
            double mbTotal = TotalBytes / (1024.0 * 1024.0);
            string etaStr = Eta.HasValue ? $"{Eta.Value.Hours:D2}:{Eta.Value.Minutes:D2}:{Eta.Value.Seconds:D2}" : "--:--:--";
            return $"{DateTime.Now:HH:mm:ss.fff} INFO : {speedMBs:F2}MB/s {mbDownloaded:F1}MB/{mbTotal:F1}MB {Percentage:F1}% {etaStr} [{DownloadedChunks}/{TotalChunks}]";
        }

        public override string ToString()
        {
            double speedMBs = SpeedBytesPerSec / (1024.0 * 1024.0);
            double mbDownloaded = DownloadedBytes / (1024.0 * 1024.0);
            double mbTotal = TotalBytes / (1024.0 * 1024.0);
            string etaStr = Eta.HasValue ? $"{Eta.Value.Hours:D2}:{Eta.Value.Minutes:D2}:{Eta.Value.Seconds:D2}" : "--:--:--";
            return $"{speedMBs:F2} MB/s | {mbDownloaded:F1} MB / {mbTotal:F1} MB ({Percentage:F1}%) | Seg: {DownloadedChunks}/{TotalChunks} | ETA: {etaStr}";
        }
    }
}

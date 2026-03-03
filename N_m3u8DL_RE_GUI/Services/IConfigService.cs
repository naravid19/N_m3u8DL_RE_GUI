#nullable enable
namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// Service for reading/writing the legacy semicolon-based config.txt format.
/// </summary>
public interface IConfigService
{
    AppConfigState Load(string path);
    void Save(string path, AppConfigState state);
}

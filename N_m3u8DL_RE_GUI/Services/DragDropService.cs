#nullable enable
using System.Windows;
using IDataObject = System.Windows.IDataObject;
using DataFormats = System.Windows.DataFormats;

namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// Implementation of drag and drop service.
/// </summary>
public class DragDropService : IDragDropService
{
    public string? HandleFileDrop(object data)
    {
        if (data is IDataObject dataObject)
        {
            if (dataObject.GetDataPresent(DataFormats.FileDrop))
            {
                var files = dataObject.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    return files[0]; // Return first file
                }
            }
            else if (dataObject.GetDataPresent(DataFormats.Text))
            {
                var text = dataObject.GetData(DataFormats.Text) as string;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }
        }

        return null;
    }

    public bool HasFiles(object data)
    {
        if (data is IDataObject dataObject)
        {
            return dataObject.GetDataPresent(DataFormats.FileDrop) || 
                   dataObject.GetDataPresent(DataFormats.Text);
        }
        return false;
    }

    public string[] GetFilePaths(object data)
    {
        if (data is IDataObject dataObject)
        {
            if (dataObject.GetDataPresent(DataFormats.FileDrop))
            {
                var files = dataObject.GetData(DataFormats.FileDrop) as string[];
                return files ?? Array.Empty<string>();
            }
            else if (dataObject.GetDataPresent(DataFormats.Text))
            {
                var text = dataObject.GetData(DataFormats.Text) as string;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return new[] { text.Trim() };
                }
            }
        }

        return Array.Empty<string>();
    }
} 
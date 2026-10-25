using Microsoft.VisualBasic.FileIO;
using Remindr.Setup.Core;

namespace Remindr.Setup;

/// <summary>Moves the data folder to the Recycle Bin so a mistaken "remove my data" can be undone.</summary>
public sealed class RecycleBinDataRemover : IDataRemover
{
    public void Remove(string dir) =>
        FileSystem.DeleteDirectory(dir, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
}

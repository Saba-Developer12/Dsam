using Dsam.Core.Analysis;
using Dsam.Core.Binary;

namespace Dsam.App.Workspace;

public sealed class DsamWorkspace : IDisposable
{
    public DsamWorkspace(
        BinaryImageDescriptor image,
        IBinaryImage binary,
        IAnalysisStore analysisStore)
    {
        Image = image;
        Binary = binary;
        AnalysisStore = analysisStore;
    }

    public BinaryImageDescriptor Image { get; }

    public IBinaryImage Binary { get; }

    public IAnalysisStore AnalysisStore { get; }

    public void Dispose() => Binary.Dispose();
}

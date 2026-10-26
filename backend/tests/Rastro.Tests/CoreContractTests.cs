using Rastro.Core;
using Rastro.Core.Graph;

namespace Rastro.Tests;

// Enums viajam como int no JSON: os valores são contrato e não podem mudar.
public class CoreContractTests
{
    [Fact]
    public void EdgeConfidence_ValoresExplicitos_NaoMudam()
    {
        Assert.Equal(0, (int)EdgeConfidence.Extracted);
        Assert.Equal(1, (int)EdgeConfidence.Inferred);
        Assert.Equal(2, (int)EdgeConfidence.Confirmed);
    }

    [Fact]
    public void ProposalOrigin_ValoresExplicitos_NaoMudam()
    {
        Assert.Equal(0, (int)ProposalOrigin.Manual);
        Assert.Equal(1, (int)ProposalOrigin.Agent);
        Assert.Equal(2, (int)ProposalOrigin.Provider);
    }

    [Fact]
    public void RastroVersion_Atual_EhIgualAoArquivoVersion()
    {
        var expected = File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "VERSION")).Trim();

        Assert.Equal(expected, RastroVersion.Current);
    }
}

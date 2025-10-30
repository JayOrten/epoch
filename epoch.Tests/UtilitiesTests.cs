using epoch.Utilities;
using Xunit;

namespace epoch.Tests;

public class UtilitiesTests
{
    [Fact]
    public void ContentPaths_SetsRootCorrectly()
    {
        // Arrange
        string newRoot = "NewContentRoot";

        // Act
        ContentPaths.SetRoot(newRoot);

        // Assert
        Assert.Equal(newRoot, ContentPaths.Root);
    }
}

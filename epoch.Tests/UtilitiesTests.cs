using Engine;
using epoch.Utilities;
using Xunit;

namespace epoch.Tests;

/// TODO: these tests weren't fun to try and develop, didn't really feel worth the effort
/// right now. Consider adding more tests later.
public class UtilitiesTests
{
    private static void ResetForTests()
    {
        ContentPaths.SetRoot(Core.Content.RootDirectory);
    }

    [Fact]
    public void ContentPaths_SetsRootCorrectly()
    {
        // Arrange
        using var core = new Engine.Core("Test", 800, 600, false);
        ResetForTests();
        string newRoot = "NewContentRootdjdjd";

        // Act
        ContentPaths.SetRoot(newRoot);

        // Assert
        Assert.Equal(newRoot, ContentPaths.Root);
    }

    [Fact]
    public void ContentPaths_GetPath()
    {
        // Arrange
        using var core = new Engine.Core("Test", 800, 600, false);
        ResetForTests();

        string testImageName = "test_image";
        string expectedPath = Path.Combine(ContentPaths.ImagesDir, $"{testImageName}.png");

        Console.WriteLine($"Expected Path: {expectedPath}");

        // Act
        string actualPath = ContentPaths.Image(testImageName);

        Console.WriteLine($"Actual Path: {actualPath}");
        // Assert
        Assert.Equal(expectedPath, actualPath);
    }
}

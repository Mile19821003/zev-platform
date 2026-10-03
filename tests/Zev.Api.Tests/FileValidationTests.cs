using System.Text;
using Xunit;
using Zev.Api.Storage;

namespace Zev.Api.Tests;

public sealed class FileValidationTests
{
    [Theory]
    [InlineData("document.pdf", "application/pdf", true)]
    [InlineData("scan.jpeg", "image/jpeg", true)]
    [InlineData("scan.png", "image/png", true)]
    [InlineData("document.exe", "application/pdf", false)]
    [InlineData("../document.pdf", "application/pdf", false)]
    [InlineData("..\\document.pdf", "application/pdf", false)]
    [InlineData("scan.jpg", "image/png", false)]
    public void FileNameMustMatchMimeAndStayWithinStorageScope(string fileName, string mime, bool expected)
    {
        Assert.Equal(expected, LocalFileStorage.ValidFileName(fileName, mime));
    }

    [Fact]
    public void LargeFileAndMismatchedSignatureAreRejected()
    {
        Assert.False(LocalFileStorage.ValidContent(new byte[5_000_001], "application/pdf"));
        Assert.False(LocalFileStorage.ValidContent(Encoding.ASCII.GetBytes("not a PDF"), "application/pdf"));
        Assert.True(LocalFileStorage.ValidContent(Encoding.ASCII.GetBytes("%PDF-1.4\n"), "application/pdf"));
    }
}

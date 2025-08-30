using TaskbarUtil.Core;
using Xunit;

namespace TaskbarUtil.Tests;

public class TaskbarItemTests
{
    [Fact]
    public void TaskbarItem_DefaultValues_AreCorrect()
    {
        var item = new TaskbarItem();
        
        Assert.Equal(string.Empty, item.Name);
        Assert.Equal(string.Empty, item.Path);
        Assert.Equal(TaskbarItemType.Unknown, item.Type);
        Assert.Equal(0, item.Position);
    }

    [Fact]
    public void TaskbarItem_SetProperties_WorksCorrectly()
    {
        var item = new TaskbarItem
        {
            Name = "Test App",
            Path = "C:\\Test\\App.exe",
            Type = TaskbarItemType.Application,
            Position = 1
        };
        
        Assert.Equal("Test App", item.Name);
        Assert.Equal("C:\\Test\\App.exe", item.Path);
        Assert.Equal(TaskbarItemType.Application, item.Type);
        Assert.Equal(1, item.Position);
    }
}

public class TaskbarItemOptionsTests
{
    [Fact]
    public void TaskbarItemOptions_DefaultValues_AreCorrect()
    {
        var options = new TaskbarItemOptions();
        
        Assert.Equal(string.Empty, options.Path);
        Assert.Null(options.Label);
        Assert.Null(options.Replacing);
        Assert.Equal(Position.End, options.PositionType);
        Assert.Null(options.Index);
        Assert.Null(options.Before);
        Assert.Null(options.After);
        Assert.Equal(TaskbarItemType.Application, options.ItemType);
    }
}

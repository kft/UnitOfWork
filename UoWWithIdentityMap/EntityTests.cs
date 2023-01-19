namespace UoWWithIdentityMap;

public class EntityTests
{
    [Fact]
    public void Reports_no_changes()
    {
        var sut = new DummyEntity(Guid.NewGuid(), "Test");
        
        Assert.False(sut.HasChanges());
    }
    
    [Fact]
    public void Reports_has_changes()
    {
        var sut = new DummyEntity(Guid.NewGuid(), "Test");

        sut.Name = "New name";
        
        Assert.True(sut.HasChanges());
    }
}

internal class DummyEntity : Entity
{
    public DummyEntity(Guid id, string name) : base(id)
    {
        Name = name;
        OriginalState = new DummyEntityState(Id, Name);
    }

    public string Name { get; set; }

    private protected override EntityState GetCurrentState()
    {
        return new DummyEntityState(Id, Name);
    }
}

internal class DummyEntityState : EntityState, IEquatable<DummyEntityState>
{
    public DummyEntityState(Guid id, string name)
    {
        Id = id;
        Name = name;
    }
    internal Guid Id { get; }
    internal string Name { get; }

    public bool Equals(DummyEntityState? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Id.Equals(other.Id) && Name == other.Name;
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((DummyEntityState)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, Name);
    }
}
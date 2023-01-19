namespace UoWWithIdentityMap;

public abstract class Entity
{
    protected Entity(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; }
    
    protected EntityState? OriginalState { get; set; }
    
    private protected abstract EntityState GetCurrentState();

    public bool HasChanges()
    {
        if (OriginalState == null) return true;
        
        return !OriginalState.Equals(GetCurrentState());
    }
}
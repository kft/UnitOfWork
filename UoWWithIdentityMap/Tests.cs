using Microsoft.Data.SqlClient;

namespace UoWWithIdentityMap;

public class Tests
{
    private readonly UnitOfWork _unitOfWork;

    public Tests()
    {
        _unitOfWork = new UnitOfWork(new List<IEntityMapper>()
        {
            new UserEntityMapper(),
        });
    }

    [Fact]
    public void Add()
    {
        var repository = new Users(_unitOfWork);
        
        var user = new User(Guid.NewGuid(),"John Doe");

        repository.Add(user);
        _unitOfWork.Commit();

        var foundUser = repository.GetById(user.Id);
        Assert.Equal(user, foundUser);
    }

    [Fact]
    public void Can_update()
    {
        var repository = new Users(_unitOfWork);
        var user = new User(Guid.NewGuid(),"John Doe");
        repository.Add(user);
        _unitOfWork.Commit();
        
        var userToUpdate = repository.GetById(user.Id);
        userToUpdate.Name = "New name";
        _unitOfWork.Commit();
        
        var foundUser = repository.GetById(user.Id);
        Assert.Equal("New name", foundUser.Name);
    }

    [Fact]
    public void If_entity_has_not_been_changed_no_database_roundtrip_is_done()
    {
        var mapperSpy = new UserEntityMapperSpy();
        var unitOfWork = new UnitOfWork(new List<IEntityMapper>() { mapperSpy });
        var repository = new Users(unitOfWork);
        var user = new User(Guid.NewGuid(),"John Doe");
        repository.Add(user);
        unitOfWork.Commit();
        
        repository.GetById(user.Id);
        unitOfWork.Commit();
        
        Assert.False(mapperSpy.UpdateEntityWasCalled);
    }

    [Fact]
    public void When_user_is_deleted_and_not_committed_it_cannot_be_fetched_again()
    {
        var repository = new Users(_unitOfWork);
        var user = new User(Guid.NewGuid(),"John Doe");
        repository.Add(user);
        _unitOfWork.Commit();
        
        var userToDelete = repository.GetById(user.Id);
        repository.Delete(userToDelete);
        
        var foundUser = repository.GetById(user.Id);
        Assert.Null(foundUser);
    }
    
    [Fact]
    public void User_is_deleted_from_database_when_unit_of_work_is_committed()
    {
        var repository = new Users(_unitOfWork);
        var user = new User(Guid.NewGuid(),"John Doe");
        repository.Add(user);
        _unitOfWork.Commit();
        
        var userToDelete = repository.GetById(user.Id);
        repository.Delete(userToDelete);
        _unitOfWork.Commit();
        
        var foundUser = repository.GetById(user.Id);
        Assert.Null(foundUser);
    }
}

public class UnitOfWork
{
    private readonly HashSet<Entity> _trackedEntities = new();
    private readonly HashSet<Entity> _newEntities = new();
    private readonly HashSet<Entity> _deletedEntities = new();
    private readonly List<IEntityMapper> _entityMappers;

    public UnitOfWork(IEnumerable<IEntityMapper> entityMappers)
    {
        _entityMappers = entityMappers.ToList();
    }
    
    public void RegisterNew(Entity entity)
    {
        _newEntities.Add(entity);
    }
    
    public void Delete(Entity entity)
    {
        if (_deletedEntities.Contains(entity))
        {
            return;
        }

        _deletedEntities.Add(entity);
        _newEntities.Remove(entity);
        _trackedEntities.Remove(entity);
    }

    public TEntity? GetById<TEntity>(Guid id)
        where TEntity : Entity
    {
        if (_deletedEntities.Any(entity => entity.Id.Equals(id)))
        {
            return null;
        }
        
        var entity = _trackedEntities.FirstOrDefault(entity => entity is TEntity && entity.Id.Equals(id));

        if (entity is null)
        {
            entity = _newEntities.FirstOrDefault(entity => entity is TEntity && entity.Id.Equals(id));
        }
        
        if (entity is null)
        {
            using var connection = OpenConnection();
            entity = MapperOf(typeof(TEntity)).FetchEntity(id, connection);
            _trackedEntities.Add(entity);
        }
        return (TEntity)entity;
    }
    
    public void Commit()
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        foreach (var entity in _newEntities)
        {
            MapperOf(entity.GetType()).InsertEntity(transaction, entity);
        }
        
        foreach (var entity in _trackedEntities)
        {
            if (entity.HasChanges())
            { 
                MapperOf(entity.GetType()).UpdateEntity(transaction, entity);   
            }
        }
        
        foreach (var entity in _deletedEntities)
        {
            MapperOf(entity.GetType()).DeleteEntity(transaction, entity);
        }
        
        transaction.Commit();
        _trackedEntities.Clear();
        _newEntities.Clear();
        _deletedEntities.Clear();
    }

    private static SqlConnection OpenConnection()
    {
        var connectionString =
            "Data Source=(LocalDB)\\MSSQLLocalDB;Initial Catalog=Test;Integrated Security=True;";
        var connection = new SqlConnection(connectionString);
        connection.Open();
        return connection;
    }

    private IEntityMapper MapperOf(Type entityType)
    {
        return _entityMappers.First(mapper => mapper.Handles(entityType));
    }
}

public interface IEntityMapper
{
    void InsertEntity(SqlTransaction transaction, Entity entity);
    void UpdateEntity(SqlTransaction transaction, Entity entity);
    void DeleteEntity(SqlTransaction transaction, Entity entity);
    Entity? FetchEntity(Guid id, SqlConnection connection);
    bool Handles(Type entityType);
}

public class UserEntityMapperSpy : UserEntityMapper
{
    public override void UpdateEntity(SqlTransaction transaction, Entity entity)
    {
        UpdateEntityWasCalled = true;
    }

    public bool UpdateEntityWasCalled { get; private set; }
}

public class UserEntityMapper : IEntityMapper
{
    public void InsertEntity(SqlTransaction transaction, Entity entity)
    {
        var user = (User)entity;
        var insertStatement = @"INSERT INTO dbo.Users (Id, Name) VALUES (@Id, @Name)";
        var insertCommand = transaction.Connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = insertStatement;
        insertCommand.Parameters.AddWithValue("@Id", user.Id);
        insertCommand.Parameters.AddWithValue("@Name", user.Name);
        insertCommand.ExecuteNonQuery();
    }
    
    public virtual void UpdateEntity(SqlTransaction transaction, Entity entity)
    {
        var user = (User)entity;
        var updateStatement = @"UPDATE dbo.Users SET Name = @Name WHERE Id = @Id";
        var updateCommand = transaction.Connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText = updateStatement;
        updateCommand.Parameters.AddWithValue("@Id", user.Id);
        updateCommand.Parameters.AddWithValue("@Name", user.Name);
        updateCommand.ExecuteNonQuery();
    }

    public void DeleteEntity(SqlTransaction transaction, Entity entity)
    {
        var user = (User)entity;
        var deleteStatement = @"DELETE FROM dbo.Users WHERE Id = @Id";
        var deleteCommand = transaction.Connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = deleteStatement;
        deleteCommand.Parameters.AddWithValue("@Id", user.Id);
        deleteCommand.ExecuteNonQuery();
    }

    public Entity? FetchEntity(Guid id, SqlConnection connection)
    {
        var selectStatement = "SELECT Id, Name FROM dbo.Users WHERE Id = @Id";
        using var selectCommand = connection.CreateCommand();
        selectCommand.CommandText = selectStatement;
        selectCommand.Parameters.AddWithValue("@Id", id);
        using var reader = selectCommand.ExecuteReader();
        while (reader.Read())
        {
            var userId = reader.GetGuid(0);
            var name = reader.GetString(1);
            return new User(userId, name);
        }

        return null;
    }

    public bool Handles(Type entity)
    {
        return entity == typeof(User);
    }
}

public class Users
{
    private readonly UnitOfWork _unitOfWork;

    public Users(UnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }
    public void Add(User user)
    {
        _unitOfWork.RegisterNew(user);
    }

    public User GetById(Guid userId)
    {
        return _unitOfWork.GetById<User>(userId);
    }

    public void Delete(User userToDelete)
    {
        _unitOfWork.Delete(userToDelete);
    }
}

public class User : Entity
{
    public User(Guid id, string name)
        : base(id)
    {
        Name = name;
        SetOriginalState();
    }
    
    public string Name { get; set; }

    public override bool Equals(object? obj)
    {
        if (obj is null)
            return false;
        
        var firstUser = this;
        var secondUser = (User)obj;
        return secondUser.Id.Equals(firstUser.Id);
    }

    private void SetOriginalState()
    {
        OriginalState = CreateState();
    }

    private protected override EntityState GetCurrentState()
    {
        return CreateState();
    }

    private UserState CreateState()
    {
        return new UserState(Id, Name);
    }
}

public class UserState : EntityState, IEquatable<UserState>
{
    public UserState(Guid id, string name)
    {
        Id = id;
        Name = name;
    }
    public Guid Id { get; }
    public string Name { get; }

    public bool Equals(UserState? other)
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
        return Equals((UserState)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, Name);
    }
}
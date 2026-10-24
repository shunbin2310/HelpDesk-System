namespace HelpDesk.Domain.Common;

public abstract class Entity
{
    public long Id { get; set; }
}

public abstract class VersionedEntity : Entity
{
    public byte[] RowVersion { get; set; } = [];
}

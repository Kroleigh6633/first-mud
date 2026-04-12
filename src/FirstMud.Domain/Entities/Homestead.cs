namespace FirstMud.Domain.Entities;

public class Homestead
{
    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int StorageSlots { get; private set; }

    private Homestead() { }

    public static Homestead Create(Guid playerId, string name, int storageSlots = 50)
    {
        return new Homestead
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            Name = name,
            StorageSlots = storageSlots,
        };
    }
}

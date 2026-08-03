namespace Lamie.Domain.Entities;

public class Language : Entity
{
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public bool IsActive { get; private set; }

    private Language() { }

    public Language(string code, string name, bool isActive = true)
    {
        Code = code;
        Name = name;
        IsActive = isActive;
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }
}


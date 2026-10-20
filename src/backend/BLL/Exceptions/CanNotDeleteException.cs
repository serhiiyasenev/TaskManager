namespace BLL.Exceptions;

public sealed class CanNotDeleteException : Exception
{
    public CanNotDeleteException(string name, int id) : base($"Entity {name} with id ({id}) cannot be deleted.") { }

    public CanNotDeleteException(string name) : base($"Entity {name} cannot be deleted.") { }
}
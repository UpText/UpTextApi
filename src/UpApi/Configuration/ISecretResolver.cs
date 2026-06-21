namespace UpApi.Configuration;

public interface ISecretResolver
{
    string Resolve(string value);
}

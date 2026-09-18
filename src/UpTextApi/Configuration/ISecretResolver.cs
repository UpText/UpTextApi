namespace UpTextApi.Configuration;

public interface ISecretResolver
{
    string Resolve(string value);
}

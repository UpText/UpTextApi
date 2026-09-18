namespace UpTextApi.Configuration;

public interface IServiceConfigResolver
{
    bool TryGet(string serviceName, out ServiceConfiguration configuration);
}

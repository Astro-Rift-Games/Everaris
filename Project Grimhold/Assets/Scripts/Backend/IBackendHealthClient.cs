using System.Threading.Tasks;

namespace Grimhold.Backend
{
    public interface IBackendHealthClient
    {
        Task<bool> CheckAsync(BackendConfiguration config);
    }
}

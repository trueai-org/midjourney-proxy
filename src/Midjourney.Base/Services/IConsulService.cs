namespace Midjourney.Base.Services
{
    public interface IConsulService
    {
        Task RegisterServiceAsync();

        Task DeregisterServiceAsync();
    }
}
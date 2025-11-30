using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Midjourney.Base
{
    public static class MediatorProvider
    {
        private static IServiceProvider _serviceProvider;

        public static void SetServiceProvider(IServiceProvider serviceProvider)
            => _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        private static IServiceScope CreateScope()
        {
            if (_serviceProvider == null)
                throw new InvalidOperationException("ServiceProvider is not set. Call MediatorProvider.SetServiceProvider(...) during startup.");

            return _serviceProvider.CreateScope();
        }

        public static async Task PublishAsync(object notification, CancellationToken cancellationToken = default)
        {
            if (notification == null)
                throw new ArgumentNullException(nameof(notification));

            using var scope = CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            // 注意：Publish 是异步的，await 它以便正确捕获异常并等待 handler 完成
            await mediator.Publish((INotification)notification, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            using var scope = CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            return await mediator.Send(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
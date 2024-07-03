namespace Midjourney.Infrastructure.Services
{
    public interface ITranslateService
    {
        string TranslateToEnglish(string prompt);

        bool ContainsChinese(string prompt);
    }
}
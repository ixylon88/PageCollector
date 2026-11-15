using PageCollector.Core;

namespace PageCollector.App.Services;

public interface ISettingsStore
{
    CollectorSettings? Load();

    void Save(CollectorSettings settings);
}
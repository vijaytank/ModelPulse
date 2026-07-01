using ModelPulse.Core.Models;

namespace ModelPulse.Core.Services.Config
{
    /// <summary>
    /// Configuration service to manage local settings load, save, and validation.
    /// Settings are stored locally in %APPDATA%\ModelPulse\settings.json (see FR-03).
    /// </summary>
    public interface IConfigService
    {
        /// <summary>
        /// Retrieves the current in-memory settings configuration.
        /// </summary>
        ModelPulseSettings CurrentSettings { get; }

        /// <summary>
        /// Loads settings from the local JSON storage file.
        /// Creates default settings if the file does not exist.
        /// Validates values against safe minimum bounds.
        /// </summary>
        void Load();

        /// <summary>
        /// Persists the current in-memory settings to the local JSON file.
        /// </summary>
        void Save();

        /// <summary>
        /// Updates the current settings with validation check.
        /// </summary>
        void UpdateSettings(ModelPulseSettings newSettings);
    }
}

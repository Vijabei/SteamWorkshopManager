using System;
using System.Collections.Generic;

namespace WorkshopManager
{
    /// <summary>
    /// The two things the app can do with a mod. They are kept apart on
    /// purpose - their own folder, their own list, their own options - so an
    /// archive layout never ends up in a game's mod folder by accident.
    /// </summary>
    public enum AppMode
    {
        /// <summary>Install mods so a game can load them.</summary>
        Install,

        /// <summary>Keep mods for preservation, with everything Steam knows about them.</summary>
        Archive
    }

    /// <summary>
    /// The options of the archive mode, stored in the settings. Everything
    /// that preserves history is on by default; the gallery is not, because
    /// it costs extra requests to Steam for every mod.
    /// </summary>
    public class ArchiveOptions
    {
        /// <summary>Name each mod's folder after its Workshop title instead of its id.</summary>
        public bool FoldersByTitle { get; set; } = true;

        /// <summary>Date files by when the mod was published, not downloaded.</summary>
        public bool WorkshopDates { get; set; } = true;

        /// <summary>Save the preview image into each mod folder.</summary>
        public bool PreviewImage { get; set; } = true;

        /// <summary>Save the other images of the Workshop page's gallery.</summary>
        public bool Gallery { get; set; } = false;

        /// <summary>Put a "Workshop page.url" shortcut into each mod folder.</summary>
        public bool WorkshopLink { get; set; } = true;

        /// <summary>Keep the mod_&lt;id&gt;.info file inside the mod folder.</summary>
        public bool InfoInside { get; set; } = true;

        /// <summary>Install a collection's mods into a folder named after it.</summary>
        public bool CollectionFolders { get; set; } = true;
    }

    /// <summary>
    /// How mods are laid out on disk during one run. Built from the settings
    /// for the current mode, so the installation code never has to know
    /// which mode it serves - only what to do.
    /// </summary>
    public class ModLayout
    {
        public AppMode Mode { get; init; } = AppMode.Install;

        public bool FoldersByTitle { get; init; }
        public bool WorkshopDates { get; init; }
        public bool PreviewImage { get; init; }
        public bool Gallery { get; init; }
        public bool WorkshopLink { get; init; }
        public bool InfoInside { get; init; }
        public bool CollectionFolders { get; init; }

        /// <summary>
        /// Merge a mod that ships a "mods" folder into the install folder, as
        /// such games expect. An archive keeps every mod in a folder of its own.
        /// </summary>
        public bool MergeModsFolder { get; init; } = true;

        /// <summary>
        /// Per-game install folders. They point into games, so they only
        /// apply when installing; an archive has its one folder.
        /// </summary>
        public IReadOnlyDictionary<string, GameRule> GameRules { get; init; } =
            new Dictionary<string, GameRule>();

        public bool IsArchive => Mode == AppMode.Archive;

        /// <summary>Plain layout games expect: id folders, nothing added.</summary>
        public static ModLayout ForInstall(Settings settings) => new()
        {
            Mode = AppMode.Install,
            GameRules = settings?.GameRules ?? new Dictionary<string, GameRule>()
        };

        public static ModLayout ForArchive(ArchiveOptions options)
        {
            options ??= new ArchiveOptions();
            return new ModLayout
            {
                Mode = AppMode.Archive,
                FoldersByTitle = options.FoldersByTitle,
                WorkshopDates = options.WorkshopDates,
                PreviewImage = options.PreviewImage,
                Gallery = options.Gallery,
                WorkshopLink = options.WorkshopLink,
                InfoInside = options.InfoInside,
                CollectionFolders = options.CollectionFolders,
                MergeModsFolder = false
            };
        }

        public static ModLayout For(Settings settings, AppMode mode) =>
            mode == AppMode.Archive ? ForArchive(settings?.Archive) : ForInstall(settings);
    }
}

/*=========================
 * Version Info Class
 *  Generate version number and build date/time
 * Versions
 *  1.0 Initial Version
 ========================*/

using System;
using System.Reflection;

namespace Samraksh.Components.Utility {
    /// <summary>
    /// Get version info
    /// </summary>
    /// <remarks>
    /// See http://stackoverflow.com/a/1601079/468523
    /// </remarks>
    public static class VersionInfo {

        /// <summary>
        /// The version for which info is required
        /// </summary>
        private static Version _theAppVersion = Assembly.GetExecutingAssembly().GetName().Version;
        private static string _theAppName = Assembly.GetExecutingAssembly().GetName().Name;
        private static readonly Version _virtualFenceAppVersion = Assembly.GetExecutingAssembly().GetName().Version;
        
        /// <summary>
        /// Initialize the version
        /// </summary>
        /// <remarks>Skip this if you want version info from the assembly in which this class resides</remarks>
        /// <param name="theAssembly"></param>
        public static void Initialize(Assembly theAssembly) {
            _theAppVersion = theAssembly.GetName().Version;
            _theAppName = theAssembly.GetName().Name;
        }

        /// <summary>
        /// Initialize assembly value and return a text string with version and build info
        /// </summary>
        /// <returns></returns>
        public static string VersionBuild(Assembly theAssembly) {
            Initialize(theAssembly);
            return VersionBuild();
        }

        /// <summary>
        /// Return a text string with version and build info
        /// </summary>
        /// <returns></returns>
        public static string VersionBuild() {
            return "Virtual Fence with Health Manager; Version: " + VirtualFenceAppVersion + "\r\nApp name: " + AppName + "; Version: " + AppVersion + ", build " + BuildDateTime;
        }

        /// <summary>
        /// Return a text string with virtual fence's version info
        /// </summary>
        public static string VirtualFenceAppVersion
        {
            get { return _virtualFenceAppVersion.Major + "." + _virtualFenceAppVersion.Minor + "." + _virtualFenceAppVersion.Build + "." + _virtualFenceAppVersion.Revision; }
        }

        /// <summary>
        /// Return a text string with application name
        /// </summary>
        public static string AppName
        {
            get { return (_theAppName.Split('.')[2]); }
        }
        /// <summary>
        /// Current build Major.Minor version info
        /// </summary>
        public static string AppVersion {
            get { return _theAppVersion.Major + "." + _theAppVersion.Minor; }
        }
        
        /// <summary>
        /// Current build DateTime
        /// </summary>
        public static DateTime BuildDateTime {
            get {
                return new DateTime(2000, 1, 1).Add(
                   new TimeSpan(TimeSpan.TicksPerDay * _theAppVersion.Build + // Days since 1 Jan 2000
                       TimeSpan.TicksPerSecond * 2 * _theAppVersion.Revision));    // Seconds since midnight
            }
        }

    }

}
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using JMS.TechnoTrend;
using JMS.TechnoTrend.MFCWrapper;


namespace JMS.DVB.Provider.TTPremium
{
    /// <summary>
    /// Manages access to the low level C++ wrappers.
    /// </summary>
    internal class Context : IDisposable
    {
        /// <summary>
        /// The one and only context. It will be created once.
        /// </summary>
        static private Context m_TheContext = null;

        /// <summary>
        /// All boot directories temporarily created.
        /// </summary>
        static private List<DirectoryInfo> m_Directories = new List<DirectoryInfo>();

        /// <summary>
        /// The device to use.
        /// </summary>
        static public int DefaultDevice = 0;

        /// <summary>
        /// Clear to initialize without network access.
        /// </summary>
        static public bool WithNetwork = true;

        /// <summary>
        /// Registered clients of the TTAPI.
        /// </summary>
        private Hashtable m_Users = new Hashtable();

        /// <summary>
        /// Cached A/V control.
        /// </summary>
        private DVBAVControl m_AudioVideo = null;

        /// <summary>
        /// Cached board control.
        /// </summary>
        private DVBBoardControl m_Board = null;

        /// <summary>
        /// Cached frontend instance.
        /// </summary>
        private DVBFrontend m_Frontend = null;

        /// <summary>
        /// Cached CI/CAM interface.
        /// </summary>
        private DVBCommonInterface m_CI = null;

        /// <summary>
        /// Remembers if device has been opened.
        /// </summary>
        private bool m_MustClose = false;

        /// <summary>
        /// Must not create instances outside this class.
        /// </summary>
        private Context()
        {
        }

        /// <summary>
        /// Forward to <see cref="Dispose"/>.
        /// </summary>
        ~Context()
        {
            // Done
            Dispose();
        }

        /// <summary>
        /// Report the <see cref="m_TheContext"/>. This property is protected by
        /// a class lock.
        /// </summary>
        /// <remarks>
        /// The <see cref="m_TheContext"/> is created once. Before the instance
        /// is constructed <see cref="Library.Initialize"/> is called.
        /// </remarks>
        static public Context TheContext
        {
            get
            {
                // Synchronize
                lock (typeof( Context ))
                    if (m_TheContext == null)
                    {
                        // Initialize library
                        Library.Initialize();

                        // Create singleton
                        m_TheContext = new Context();
                    }

                // Report
                return m_TheContext;
            }
        }

        /// <summary>
        /// Cleanup all access to the various cached instances.
        /// </summary>
        /// <remarks>
        /// The most important task is to call <see cref="Dispose"/> on
        /// all objects cached. After that all references are released.
        /// If <see cref="m_MustClose"/> is set finally <see cref="CloseDevice"/>
        /// is called.
        /// </remarks>
        public void Dispose()
        {
            // Shutdown all
            if (m_AudioVideo != null)
                using (m_AudioVideo)
                {
                    // Shut up
                    SetPIDs( 0, 0 );

                    // Done
                    m_AudioVideo = null;
                }
            using (m_CI)
                m_CI = null;
            using (m_Frontend)
                m_Frontend = null;
            using (m_Board)
                m_Board = null;

            // Detach
            m_Users.Clear();
            m_AudioVideo = null;

            // Check for close
            if (m_MustClose)
                CloseDevice();
        }

        /// <summary>
        /// Read <see cref="m_Board"/>. This property is synchronized.
        /// </summary>
        /// <remarks>
        /// The instance will be created on the first call.
        /// </remarks>
        private DVBBoardControl Board
        {
            get
            {
                // Synchronizes
                lock (this)
                    if (m_Board == null)
                        m_Board = new DVBBoardControl();

                // Report
                return m_Board;
            }
        }

        /// <summary>
        /// Report the singleton common interface instance.
        /// </summary>
        private DVBCommonInterface CI
        {
            get
            {
                // Synchronize
                lock (this)
                    if (m_CI == null)
                        m_CI = new DVBCommonInterface();

                // Report
                return m_CI;
            }
        }

        /// <summary>
        /// Read <see cref="m_Frontend"/>. This property is synchronized.
        /// </summary>
        /// <remarks>
        /// On the first call the instance is created and <see cref="DVBFrontend.Initialize"/>
        /// will be called automatically.
        /// </remarks>
        private DVBFrontend Frontend
        {
            get
            {
                // Synchronize
                lock (this)
                    if (m_Frontend == null)
                    {
                        // Attach
                        m_Frontend = new DVBFrontend();

                        // Startup
                        m_Frontend.Initialize();
                    }

                // Report
                return m_Frontend;
            }
        }

        /// <summary>
        /// Read <see cref="m_AudioVideo"/>. This property is synchronized.
        /// </summary>
        /// <remarks>
        /// The instance will be created on the first call. In addition 
        /// <see cref="DVBAVControl.Initialize"/> will be called
        /// automatically.
        /// </remarks>
        private DVBAVControl AudioVideo
        {
            get
            {
                // Synchronize
                lock (this)
                    if (m_AudioVideo == null)
                    {
                        // Attach
                        m_AudioVideo = new DVBAVControl();

                        // Starup
                        m_AudioVideo.Initialize();
                    }

                // Report
                return m_AudioVideo;
            }
        }

        /// <summary>
        /// Uses <see cref="DVBCommon.CloseDevice"/> to close the current device.
        /// </summary>
        /// <exception cref="DVBException">
        /// Will be called if <see cref="m_MustClose"/> is not set.
        /// </exception>
        public void CloseDevice()
        {
            // Check it
            if (!m_MustClose)
                throw new DVBException( "No Device open" );

            // Once
            m_MustClose = false;

            // Process
            DVBCommon.CloseDevice();

            // Time to cleanup directories
            foreach (var dir in m_Directories)
                if (dir.Exists)
                    dir.Delete( true );

            // Forget about it all
            m_Directories.Clear();
        }

        /// <summary>
        /// Open the indicated device using <see cref="DVBCommon.OpenDevice"/>.
        /// </summary>
        /// <param name="lIndex">Zero-based index of the device.</param>
        /// <param name="sApp">Name of the current application - may be null.</param>
        /// <param name="bNoNet">[Not (yet) known]</param>
        /// <exception cref="DVBException">
        /// Thrown when <see cref="m_MustClose"/> is set.
        /// </exception>
        public void OpenDevice( int lIndex, string sApp, bool bNoNet )
        {
            // Already did it
            if (m_MustClose)
                throw new DVBException( "Must close Device first" );

            // Forward
            DVBCommon.OpenDevice( lIndex, sApp, bNoNet );

            // Remember
            m_MustClose = true;
        }

        /// <summary>
        /// Call the <see cref="OpenDevice(int, string, bool)"/> overload with <i>false</i> as the last parameter.
        /// </summary>
        /// <param name="lIndex">Zero-based index of the device.</param>
        /// <param name="sApp">Name of the current application - may be null.</param>
        public void OpenDevice( int lIndex, string sApp )
        {
            // Forward
            OpenDevice( lIndex, sApp, false );
        }

        /// <summary>
        /// Call the <see cref="OpenDevice(int, string)"/> overload with <i>null</i> as the last parameter.
        /// </summary>
        /// <param name="lIndex">Zero-based index of the device.</param>
        public void OpenDevice( int lIndex )
        {
            // Forward
            OpenDevice( lIndex, null );
        }

        /// <summary>
        /// Forwards to <see cref="DVBCommon.GetNumberOfDevices"/>.
        /// </summary>
        public int NumberOfDevices { get { return DVBCommon.GetNumberOfDevices(); } }

        /// <summary>
        /// Forwards to <see cref="DVBBoardControl.BootARM(out DirectoryInfo)"/> on <see cref="Board"/>.
        /// </summary>
        public void BootARM()
        {
            // New directory to cleanup later on
            DirectoryInfo bootDir;

            // Load code
            Board.BootARM( out bootDir );

            // Remember
            if (bootDir != null)
                m_Directories.Add( bootDir );
        }

        /// <summary>
        /// Prepare a standard connection.
        /// </summary>
        /// <param name="sApp">Current application - may be <i>null</i>.</param>
        public void StartDefault( string sApp )
        {
            // Open default device
            OpenDevice( DefaultDevice, sApp, !WithNetwork );

            // Load code
            BootARM();

            // Time to set up frontend - will automatically initialize
            var pLoad = Frontend;

            // Run with DMA on
            Board.DataDMA = true;

            // On all
            AudioVideo.VideoOutput = VideoOutput.CVBS_RGB;

            // Choose digital source
            if ((AVCapabilities.Analog & AudioVideo.Capabilities) == AVCapabilities.Analog)
                AudioVideo.ADSwitch = DeviceInput.DVB;

            // Switch off picture and MP2
            SetPIDs( 0, 0 );
        }

        /// <summary>
        /// Forward to <see cref="DVBFrontend.FrontendType"/> on <see cref="Frontend"/>.
        /// </summary>
        public FrontendType FrontendType { get { return Frontend.FrontendType; } }

        /// <summary>
        /// W�hlt eine Quellgruppe aus.
        /// </summary>
        /// <param name="group">D�e Daten der Quellgruppe.</param>
        /// <param name="location">Die Wahl des Ursprungs, �ber den die Quellgruppe empfangen werden kann.</param>
        /// <returns>Gesetzt, wenn es sich um eine DVB-S Quellgruppe handelt.</returns>
        public void SetChannel( SourceGroup group, GroupLocation location )
        {
            // Always stop CI
            Decrypt( 0 );

            // Store
            Frontend.SetChannel( group, location );
        }

        /// <summary>
        /// Start or stop decryption.
        /// </summary>
        /// <param name="serviceIdentifier">May be <i>0</i> to stop decryption. Normally
        /// this will be the service identifier for the program to decrypt.</param>
        public void Decrypt( ushort serviceIdentifier )
        {
            // Start if needed
            if (serviceIdentifier == 0)
                return;

            // Access CI once
            if (CI.IsIdle)
                CI.Open();

            // Start decryption
            CI.Decrypt( serviceIdentifier );
        }

        /// <summary>
        /// Legt die Datenstr�me f�r die Anzeige fest.
        /// </summary>
        /// <param name="uAudio">Datenstromkennung der Tonspur.</param>
        /// <param name="uVideo">Datenstromkennung der Bildspur.</param>
        public void SetPIDs( ushort uAudio, ushort uVideo )
        {
            // Forward
            AudioVideo.SetPIDs( uAudio, uVideo, 0 );
        }

        /// <summary>
        /// Add the client reference to the <see cref="m_Users"/>. This method
        /// is synchronized.
        /// </summary>
        /// <remarks>
        /// When the first client is added and <see cref="DVBCommon.IsOpen"/>
        /// reports <i>false</i> <see cref="StartDefault"/> is called with 
        /// a <i>null</i> argument.
        /// <seealso cref="UnRegister"/>
        /// </remarks>
        /// <param name="pClient">Some client used as key.</param>
        public void Register( object pClient )
        {
            // Synchronize
            lock (this)
            {
                // Remember
                m_Users[pClient] = pClient;

                // Already active
                if (m_Users.Count > 1)
                    return;

                // Start us
                if (!DVBCommon.IsOpen)
                    StartDefault( null );
            }
        }

        /// <summary>
        /// Removes a client registered with <see cref="Register"/>.
        /// </summary>
        /// <remarks>
        /// When <see cref="m_Users"/> becomes empty <see cref="Dispose"/>
        /// is called.
        /// </remarks>
        /// <param name="pClient">Some client used as key.</param>
        public void UnRegister( object pClient )
        {
            // Synchronize
            lock (this)
            {
                // Discard
                m_Users.Remove( pClient );

                // Clear
                if (m_Users.Count < 1)
                    Dispose();
            }
        }

        /// <summary>
        /// Report <see cref="DVBFrontend.Filter"/> from our
        /// <see cref="Frontend"/>.
        /// </summary>
        public DVBFrontend._Filters RawFilter { get { return Frontend.Filter; } }

        /// <summary>
        /// Simply call <see cref="DVBFrontend.StopFilters"/> on our <see cref="Frontend"/>.
        /// </summary>
        public void StopFilters()
        {
            // Forward
            StopFilters( false );
        }

        /// <summary>
        /// Simply call <see cref="DVBFrontend.StopFilters"/> on our <see cref="Frontend"/>.
        /// </summary>
        /// <param name="initialize">Set to re-initialize the frontend.</param>
        public void StopFilters( bool initialize )
        {
            // Check mode
            if (initialize)
                Frontend.Initialize();
            else
                Frontend.StopFilters();
        }

        /// <summary>
        /// Meldet die Informationen zum aktuellen Signal.
        /// </summary>
        public DVBSignalStatus SignalStatus { get { return Frontend.SignalStatus; } }
    }
}

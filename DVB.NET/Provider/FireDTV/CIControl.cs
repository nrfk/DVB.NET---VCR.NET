﻿using System;
using System.Threading;
using JMS.DVB.DeviceAccess;
using JMS.DVB.DeviceAccess.Interfaces;
using JMS.DVB.DeviceAccess.Pipeline;


namespace JMS.DVB.Provider.FireDTV
{
    /// <summary>
    /// Diese Klasse steuert das CI einer FireDTV an.
    /// </summary>
    [
        Pipeline( PipelineTypes.CICAM, "FireDTV" )
    ]
    public class CIControl : IPipelineExtension
    {
        /// <summary>
        /// Die eindeutige Kennung des PropertySets für die FireDTV.
        /// </summary>
        internal static readonly Guid PropertySetId = new Guid( "ab132414-d060-11d0-8583-00c04fd9baf3" );

        /// <summary>
        /// Zählt die Aufrufe der Entschlüsselungsmethode <see cref="Decrypt(DataGraph.DecryptToken)"/>.
        /// </summary>
        private int m_ChangeCounter;

        /// <summary>
        /// Der zuletzt beobachtete BDA Graph.
        /// </summary>
        private DataGraph m_DataGraph;

        /// <summary>
        /// Steuert den Zugriff auf die Hardware.
        /// </summary>
        private readonly object m_deviceAccess = new object();

        /// <summary>
        /// Wird zur eigentlichen Steuerung der Entschlüsselung aufgerufen.
        /// </summary>
        /// <param name="pmt">Die Informationen zur Quelle.</param>
        /// <param name="reset">Gesetzt, um einen Neustart der Entschlüsselung zu erzwingen.</param>
        private void Decrypt( EPG.Tables.PMT pmt, bool reset )
        {
            // Check the interface
            var setPtr = ComIdentity.QueryInterface( m_DataGraph.TunerFilter.Interface, typeof( KsPropertySetFireDTV.Interface ) );
            if (setPtr == IntPtr.Zero)
                return;

            // Process
            using (var propertySet = new KsPropertySetFireDTV( setPtr ))
                try
                {
                    // Load property identifier
                    var propSetId = PropertySetId;
                    var supported = propertySet.QuerySupported( ref propSetId, BDAProperties.SendCA );
                    if ((PropertySetSupportedTypes.Set & supported) != PropertySetSupportedTypes.Set)
                        return;

                    // Process reset
                    if (reset)
                        propertySet.Send( CACommand.CreateReset() );
                    else if (pmt == null)
                        propertySet.Send( CACommand.StopDecryption() );
                    else
                        propertySet.Send( CACommand.CreateDecrypt( pmt ) );
                }
                catch
                {
                    // Forward if not resetting
                    if (pmt != null)
                        throw;
                }
        }

        /// <summary>
        /// Aktiviert die Entschlüsselung einer Quelle.
        /// </summary>
        /// <param name="token">Informationen zur gewählten Quelle.</param>
        private PipelineResult Decrypt( DataGraph.DecryptToken token )
        {
            // Load graph
            if (token != null)
                m_DataGraph = token.Pipeline.Graph;

            // Get unique call identifier
            var callIdentifier = Interlocked.Increment( ref m_ChangeCounter );

            // Check mode of operation
            var sources = (token == null) ? null : token.Sources;
            var noSources = (sources == null) || (sources.Length < 1);
            if (noSources)
            {
                // Shutdown all
                if (m_DataGraph != null)
                    if (m_DataGraph.TunerFilter != null)
                        lock (m_deviceAccess)
                            Decrypt( null, (token == null) || (sources == null) );

                // Next
                return PipelineResult.Continue;
            }

            // Start processor
            token.WaitForPMTs(
                ( pmt, first ) =>
                {
                    // See if we are still allowed to process and do so
                    lock (m_deviceAccess)
                        if (Thread.VolatileRead( ref m_ChangeCounter ) == callIdentifier)
                            Decrypt( pmt, false );
                        else
                            return false;

                    // Next
                    return true;
                }, sources );

            // Next
            return PipelineResult.Continue;
        }

        #region IPipelineExtension Members

        /// <summary>
        /// Aktualisiert die Aktionslisten des DVB Empfangs.
        /// </summary>
        /// <param name="graph">Der DirectShow Graph für den DVB Empfang.</param>
        /// <param name="profile">Das verwendete Geräteprofil.</param>
        /// <param name="types">Die gewünschte Aktivierung.</param>
        void IPipelineExtension.Install( DataGraph graph, Profile profile, PipelineTypes types )
        {
            // Validate
            if (graph == null)
                throw new ArgumentNullException( "graph" );
            if (profile == null)
                throw new ArgumentNullException( "profile" );

            // Check supported types
            if ((types & PipelineTypes.CICAM) != 0)
                graph.DecryptionPipeline.AddPostProcessing( Decrypt );
        }

        #endregion
    }
}

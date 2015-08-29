using System.Collections.Generic;

namespace SLPLoader
{
	public static class Strukturen
	{
		public struct Header
		{
			/// <summary>
			/// Die Version der SLP. Länge: 4
			/// </summary>
			/// <remarks></remarks>
			public string Version;

			/// <summary>
			/// Die Anzahl der Frames.
			/// </summary>
			/// <remarks></remarks>
			public uint Frameanzahl;

			/// <summary>
			/// Ein Kommentar-String der Länge 24.
			/// </summary>
			/// <remarks></remarks>
			public string Kommentar;
		}

		public struct FrameInformationenHeader
		{
			public uint FrameKommandoOffset;
			public uint FrameUmrissOffset;
			public uint PaletteOffset;
			public uint Eigenschaften;
			public uint Breite;
			public uint Höhe;
			public int XAnker;
			public int YAnker;
		}

		public struct FrameInformationenDaten
		{
			/// <summary>
			/// Größe: FIH-&gt;Höhe, 2.
			/// </summary>
			/// <remarks></remarks>
			public ushort[,] RowEdge;

			/// <summary>
			/// Größe: FIH-&gt;Höhe.
			/// </summary>
			/// <remarks></remarks>
			public uint[] KommandoTabelleOffsets;

			/// <summary>
			/// Enthält die umgesetzten Kommandodaten des Frames.
			/// Größe: FIH-&gt;Höhe, FIH-&gt;Breite.
			/// </summary>
			/// <remarks></remarks>
			public int[,] KommandoTabelle;

			/// <summary>
			/// Die Original-RowEdge-Daten (für den Speichervorgang).
			/// Größe: FIH-&gt;Höhe, 2.
			/// </summary>
			/// <remarks></remarks>
			public Loader.binaryRowedge[] BinaryRowEdge;

			/// <summary>
			/// Die später geschriebene Kommando-Tabelle mit den Kommando-Bytes (für den Speichervorgang).
			/// </summary>
			public List<Loader.binaryCommand> BinaryCommandTable;
		}
	}
}
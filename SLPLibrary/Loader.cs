using BitmapLibrary;
using IORAMHelper;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

/// <summary>
/// Diese Klasse repräsentiert eine SLP-Datei.
/// </summary>
/// <remarks></remarks>
namespace SLPLoader
{
	public class Loader
	{
		#region Definitionen

		/// <summary>
		/// Definiert die ID der enthaltenen SLP-Datei.
		/// </summary>
		/// <remarks></remarks>
		private ushort _resourceID;

		/// <summary>
		/// Die SLP-Datei.
		/// </summary>
		/// <remarks></remarks>
		private RAMBuffer _dataBuffer;

		/// <summary>
		/// Gibt das aktuelle Puffer-Objekt zurück.
		/// </summary>
		/// <value></value>
		/// <returns></returns>
		/// <remarks></remarks>
		public object PufferDaten
		{
			get
			{
				return _dataBuffer.Clone();
			}
		}

		/// <summary>
		/// Die aus der SLP gelesenen Einstellungen.
		/// </summary>
		public Settings _settings;

		public Strukturen.Header _headers = new Strukturen.Header();
		public List<Strukturen.FrameInformationenHeader> _frameInformationenHeaders = new List<Strukturen.FrameInformationenHeader>();
		public List<Strukturen.FrameInformationenDaten> _frameInformationenDaten = new List<Strukturen.FrameInformationenDaten>();

		// Umgesetzte Befehlswerte. Übernommen vom Mod Workshop.
		private const byte _maske = 0; // -1

		private const byte _outline1 = 8; // -2
		private const byte _outline2 = 124; // -3
		private const byte _schatten = 131;  // -4

		#endregion Definitionen

		#region Funktionen

		/// <summary>
		/// Lädt eine angegebene SLP-Datei.
		/// </summary>
		/// <param name="Data">Eine Instanz der PufferKlasse-Klasse mit den SLP-Daten.</param>
		/// <remarks></remarks>
		public Loader(RAMBuffer Data)
		{
			_dataBuffer = Data;
			_dataBuffer.Position = 0;
			loadData();
		}

		/// <summary>
		/// Lädt die SLP-Daten.
		/// </summary>
		/// <remarks></remarks>
		private void loadData()
		{
			// Header

			#region SLP-Header

			_headers.Version = _dataBuffer.ReadString(4);
			_headers.Frameanzahl = ReadUInteger();
			_headers.Kommentar = _dataBuffer.ReadString(24);

			#endregion SLP-Header

			// Frame-Informationen: Header

			#region Frame-Header

			for(int i = 0; i < _headers.Frameanzahl; i++)
			{
				// Neues Frame-Header-Objekt erstellen
				Strukturen.FrameInformationenHeader aktFIH = new Strukturen.FrameInformationenHeader();

				// Der Zeichenindex in der SLP-Datei, an dem die Kommandotabelle des Frames beginnt
				aktFIH.FrameKommandoOffset = ReadUInteger();

				// Der Zeichenindex in der SLP-Datei, an dem die Umrissdaten (RowEdge) des Frames gespeichert sind
				aktFIH.FrameUmrissOffset = ReadUInteger();

				// Der Zeichenindex in der SLP-Datei, an dem die Farbpalette des Frames definiert ist; Genaueres ist nicht bekannt
				aktFIH.PaletteOffset = ReadUInteger();

				// Die Frame-Eigenschaften; die Bedeutung dieses Werts ist unbekannt
				aktFIH.Eigenschaften = ReadUInteger();

				// Die Abmessungen des Frames
				aktFIH.Breite = ReadUInteger();
				aktFIH.Höhe = ReadUInteger();

				// Die Anker (Mittelpunkt) des Frames
				aktFIH.XAnker = ReadInteger();
				aktFIH.YAnker = ReadInteger();

				// Frame-Header in die zentrale Liste schreiben
				_frameInformationenHeaders.Add(aktFIH);
			}

			#endregion Frame-Header

			// Frameinformationen: Daten

			#region Frame-Daten (RowEdge und Kommandotabelle)

			// Gefundene Einstellungen merken
			bool useTransp = false;
			bool useOutline1 = false;
			bool useOutline2 = false;
			bool usePlayerColor = false;
			bool useShadow = false;

			// Frames einzeln durchgehen
			for(int i = 0; i < _headers.Frameanzahl; i++)
			{
				// Spielernummer einer ggf. ausgegebenen Einheit
				const byte Spielernummer = 0; // Spieler 1 => Blau

				// Frame-Header-Daten abrufen
				Strukturen.FrameInformationenHeader aktFIH = _frameInformationenHeaders[i];

				// Neues Frame-Daten-Objekt erstellen
				Strukturen.FrameInformationenDaten aktFID = new Strukturen.FrameInformationenDaten();

				// RowEdge (leere Fläche [=> Transparenz, ohne Kommandos], von den Bildrändern ausgehend)

				#region RowEdge-Daten

				// Arrays initialisieren: Das RowEdge-Array und das BinaryRowEdge-Array, um bei unveränderten Frames dieses nicht neu berechnen zu müssen und direkt schreiben zu können
				aktFID.RowEdge = new ushort[aktFIH.Höhe, 2];
				aktFID.BinaryRowEdge = new binaryRowedge[aktFIH.Höhe];
				for(int j = 0; j < aktFIH.Höhe; j++)
				{
					// Werte einlesen
					ushort left = ReadUShort(); // Links
					ushort right = ReadUShort(); // Rechts

					// Evtl. falsche Werte korrigieren
					{
						// Links
						if(left > aktFIH.Breite)
						{
							left = (ushort)aktFIH.Breite;
						}

						// Rechts
						if(right > aktFIH.Breite)
						{
							right = (ushort)aktFIH.Breite;
						}
					}

					// Werte speichern
					aktFID.RowEdge[j, 0] = left;
					aktFID.RowEdge[j, 1] = right;

					// Binäres RowEdge mitschreiben
					aktFID.BinaryRowEdge[j] = new binaryRowedge(left, right);
				}

				#endregion RowEdge-Daten

				// Kommandotabellen-Offsets abrufen

				#region Kommandotabellen-Offsets

				aktFID.KommandoTabelleOffsets = new uint[aktFIH.Höhe];
				for(int j = 0; j < aktFIH.Höhe; j++)
				{
					aktFID.KommandoTabelleOffsets[j] = ReadUInteger();
				}

				#endregion Kommandotabellen-Offsets

				// Kommandotabelle zeilenweise auslesen und umgewandelte Kommandotabelle erzeugen

				#region Kommandotabelle

				// Gleichzeitig wird die binaryCommands-Variable miterzeugt, um unveränderte Frames ggf. ohne Umweg über eine Neuerstellung der Kommandos wieder in die *.slp schreiben zu können
				aktFID.KommandoTabelle = new int[aktFIH.Höhe, aktFIH.Breite];
				aktFID.BinaryCommandTable = new List<binaryCommand>();
				for(int j = 0; j < aktFIH.Höhe; j++)
				{
					// Gibt die aktuelle X-Position innerhalb des generierten Bildes (in Pixeln) an
					int aktPtr = 0;

					// Linker RowEdge-Bereich ist leer => transparent, also -1
					for(int k = 0; k < aktFID.RowEdge[j, 0]; k++)
					{
						aktFID.KommandoTabelle[j, aktPtr + k] = -1;
					}

					// Linker RowEdge-Bereich ist komplett in das Bild geschrieben worden
					aktPtr += aktFID.RowEdge[j, 0];

					// Wird false, wenn ein Zeilenumbruch erreicht wurde
					bool Weiter = true;

					// Kommando für Kommando lesen
					while(Weiter)
					{
						// Das aktuelle Kommandobyte
						byte aktKommandoByte = ReadByte();

						// Das aktuelle Kommando (die ersten vier Bits des Kommandobytes) Wird berechnet, um das oft mit der Datenlänge korrelierte Kommandobyte bestimmen zu können (es müssen nur die Werte 0 bis 15 abgefragt werden, statt eigentlich 0 bis 255; weiterhin geben immer die ersten 2 oder 4 Bits das Kommando an, die anderen 4 oder 6 meist nur die jeweilige Länge)
						byte aktKommando = (byte)(aktKommandoByte & 0x0F);

						// Das Kommando auswerten
						switch(aktKommando)
						{
							// Farbblock (klein)
							case 0x00:
							case 0x04:
							case 0x08:
							case 0x0C:
								{
									// Länge ermitteln
									int len = aktKommandoByte >> 2;

									// Daten einlesen
									byte[] dat = ReadBytes(len);

									// Daten in die Kommandotabelle schreiben
									for(int k = 0; k < len; k++)
									{
										aktFID.KommandoTabelle[j, aktPtr + k] = dat[k];
									}

									// Es wurden len Pixel eingelesen
									aktPtr += len;

									// Binary-Wert erstellen
									cmdColor(ref aktFID, dat);

									break;
								}

							// Transparenz (klein)
							case 0x01:
							case 0x05:
							case 0x09:
							case 0x0D:
								{
									// Länge ermitteln
									int len = aktKommandoByte >> 2;

									// Transparenz in die Kommandotabelle schreiben
									for(int k = 0; k < len; k++)
									{
										aktFID.KommandoTabelle[j, aktPtr + k] = -1;
									}

									// Es wurden len Pixel eingelesen
									aktPtr += len;

									// Binary-Wert erstellen
									cmdTransp(ref aktFID, len);

									// Transparenz wurde gefunden
									useTransp = true;

									break;
								}

							// Großer Farbblock
							case 0x02:
								{
									// Hilfs-Kommandobyte auslesen
									byte byte2 = ReadByte();

									// Länge ermitteln
									int len = ((aktKommandoByte & 0xF0) << 4) + byte2;

									// Daten einlesen
									byte[] dat = ReadBytes(len);

									// Daten in die Kommandotabelle schreiben
									for(int k = 0; k < len; k++)
									{
										aktFID.KommandoTabelle[j, aktPtr + k] = dat[k];
									}

									// Es wurden len Pixel eingelesen
									aktPtr += len;

									// Binary-Wert erstellen
									cmdColor(ref aktFID, dat);

									break;
								}

							// Großer Transparenzblock
							case 0x03:
								{
									// Hilfs-Kommandobyte auslesen
									byte byte2 = ReadByte();

									// Länge ermitteln
									int len = ((aktKommandoByte & 0xF0) << 4) + byte2;

									// Transparenz in die Kommandotabelle schreiben
									for(int k = 0; k < len; k++)
									{
										aktFID.KommandoTabelle[j, aktPtr + k] = -1;
									}

									// Es wurden len Pixel eingelesen
									aktPtr += len;

									// Binary-Wert erstellen
									cmdTransp(ref aktFID, len);

									// Transparenz wurde gefunden
									useTransp = true;

									break;
								}

							// Spielerfarbenblock
							case 0x06:
								{
									// Die oberen 4 Bits bestimmen
									byte next4Bits = (byte)(aktKommandoByte & 0xF0);

									// Die Länge der Daten
									int len = 0;

									// Die oberen 4 Bits sind 0, wenn die Länge im nächsten Byte angegeben ist; ansonsten werden diese um 4 nach rechts verschoben und sind somit als Längenangabe verwendbar
									if(next4Bits == 0)
									{
										len = ReadByte();
									}
									else
									{
										len = next4Bits >> 4;
									}

									// Daten auslesen
									byte[] dat = ReadBytes(len);

									// Daten durchgehen
									for(int k = 0; k < len; k++)
									{
										// Das aktuelle Byte
										byte aktVal = dat[k];

										// Farbindex berechnen: Die Spielerfarben liegen zwischen 16 und 13. Beim SLP-Schreiben wurde von allen Werten 16 subtrahiert => aktVal liegt zwischen 0 und 7. Zwischen den Spielerfarben auf der Palette liegen immer 16 Einheiten. Angegeben wird die Spielerfarbe durch: 16 + (Spielernummer - 1) * 16, wobei die Spielernummer zwischen 1 und 8 liegt.
										aktVal = (byte)(aktVal + 16 + 16 * Spielernummer);

										// Wert in das Bild schreiben
										aktFID.KommandoTabelle[j, aktPtr + k] = aktVal;

										// Wert zurück in Array schreiben, um das Schreiben der Binary-Daten zu erleichtern
										dat[k] += 16;
									}

									// Es wurden len Pixel eingelesen
									aktPtr += len;

									// Binary-Wert erstellen
									cmdPlayerColor(ref aktFID, dat);

									// Spielerfarben wurden gefunden
									usePlayerColor = true;

									break;
								}

							// Einfarbiger Block
							case 0x07:
								{
									// Die oberen 4 Bits bestimmen
									byte next4Bits = (byte)(aktKommandoByte & 0xF0);

									// Die Länge der Daten
									int len = 0;

									// Die oberen 4 Bits sind 0, wenn die Länge im nächsten Byte angegeben ist; ansonsten werden diese um 4 nach rechts verschoben und sind somit als Längenangabe verwendbar
									if(next4Bits == 0)
									{
										len = ReadByte();
									}
									else
									{
										len = next4Bits >> 4;
									}

									// Das nächste Byte gibt die Farbe an
									byte farbe = ReadByte();

									// Hilfsvariable für das Binary-Schreiben
									byte[] dat = new byte[len];

									// Betroffene Pixel durchgehen
									for(int k = 0; k < len; k++)
									{
										// Farbe in das Bild schreiben
										aktFID.KommandoTabelle[j, aktPtr + k] = farbe;

										// Farbe in das Array übernehmen
										dat[k] = farbe;
									}

									// Es wurden len Pixel eingelesen
									aktPtr += len;

									// Binary-Wert erstellen
									cmdColor(ref aktFID, dat);

									break;
								}

							// Einfarbiger Spielerfarben-Block
							case 0x0A:
								{
									// Die oberen 4 Bits bestimmen
									byte next4Bits = (byte)(aktKommandoByte & 0xF0);

									// Die Länge der Daten
									int len = 0;

									// Die oberen 4 Bits sind 0, wenn die Länge im nächsten Byte angegeben ist; ansonsten werden diese um 4 nach rechts verschoben und sind somit als Längenangabe verwendbar
									if(next4Bits == 0)
									{
										len = ReadByte();
									}
									else
									{
										len = next4Bits >> 4;
									}

									// Das nächste Byte gibt die Grundfarbe an
									byte farbe = ReadByte();

									// Hilfsvariable für das Binary-Schreiben
									byte[] dat = new byte[len];

									// Daten durchgehen
									for(int k = 0; k < len; k++)
									{
										// Das aktuelle Byte
										byte aktVal = farbe;

										// Farbindex berechnen: Die Spielerfarben liegen zwischen 16 und 13. Beim SLP-Schreiben wurde von allen Werten 16 subtrahiert => aktVal liegt zwischen 0 und 7. Zwischen den Spielerfarben auf der Palette liegen immer 16 Einheiten. Angegeben wird die Spielerfarbe durch: 16 + (Spielernummer - 1) * 16, wobei die Spielernummer zwischen 1 und 8 liegt.
										aktVal = (byte)(aktVal + 16 + 16 * Spielernummer);

										// Wert in das Bild schreiben
										aktFID.KommandoTabelle[j, aktPtr + k] = aktVal;

										// Wert zurück in Array schreiben, um das Schreiben der Binary-Daten zu erleichtern
										dat[k] = (byte)(farbe + 16);
									}

									// Es wurden len Pixel eingelesen
									aktPtr += len;

									// Binary-Wert erstellen
									cmdPlayerColor(ref aktFID, dat);

									// Spielerfarben wurden gefunden
									usePlayerColor = true;

									break;
								}

							// Schattenblock
							case 0x0B:
								{
									// Die oberen 4 Bits bestimmen
									byte next4Bits = (byte)(aktKommandoByte & 0xF0);

									// Die Länge der Daten
									int len = 0;

									// Die oberen 4 Bits sind 0, wenn die Länge im nächsten Byte angegeben ist; ansonsten werden diese um 4 nach rechts verschoben und sind somit als Längenangabe verwendbar
									if(next4Bits == 0)
									{
										len = ReadByte();
									}
									else
									{
										len = next4Bits >> 4;
									}

									// Schattenpixel einzeln durchgehen
									for(int k = 0; k < len; k++)
									{
										// Schatten in Bild schreiben
										aktFID.KommandoTabelle[j, aktPtr + k] = -4;
									}

									// Es wurden len Pixel eingelesen
									aktPtr += len;

									// Binary-Wert erstellen
									cmdShadow(ref aktFID, len);

									// Schatten wurde gefunden
									useShadow = true;

									break;
								}

							// Outline-Kommandos: Hängen von Kommandobyte ab ("E" am Ende bei allen gemeinsam)
							case 0x0E:
								{
									switch(aktKommandoByte)
									{
										// Outline1-Pixel
										case 0x4E:
											{
												// Outline auf Bild schreiben
												aktFID.KommandoTabelle[j, aktPtr] = -2;

												// Es wurde ein Pixel eingelesen
												aktPtr += 1;

												// Binary-Wert erstellen
												cmdOutline1(ref aktFID, 1);

												// Outline1 wurde gefunden
												useOutline1 = true;

												break;
											}

										// Outline2-Pixel
										case 0x6E:
											{
												// Outline auf Bild schreiben
												aktFID.KommandoTabelle[j, aktPtr] = -3;

												// Es wurde ein Pixel eingelesen
												aktPtr += 1;

												// Binary-Wert erstellen
												cmdOutline2(ref aktFID, 1);

												// Outline2 wurde gefunden
												useOutline2 = true;

												break;
											}

										// Outline1-Block
										case 0x5E:
											{
												// Blocklänge abrufen
												int len = ReadByte();

												// Outlinepixel in der angegebenen Anzahl auf das Bild schreiben
												for(int k = 0; k < len; k++)
												{
													aktFID.KommandoTabelle[j, aktPtr + k] = -2;
												}

												// Es wurden len Pixel eingelesen
												aktPtr += len;

												// Binary-Wert erstellen
												cmdOutline1(ref aktFID, len);

												// Outline1 wurde gefunden
												useOutline1 = true;

												break;
											}

										// Outline2-Block
										case 0x7E:
											{
												// Blocklänge abrufen
												int len = ReadByte();

												// Outlinepixel in der angegebenen Anzahl auf das Bild schreiben
												for(int k = 0; k < len; k++)
												{
													aktFID.KommandoTabelle[j, aktPtr + k] = -3;
												}

												// Es wurden len Pixel eingelesen
												aktPtr += len;

												// Binary-Wert erstellen
												cmdOutline2(ref aktFID, len);

												// Outline2 wurde gefunden
												useOutline2 = true;

												break;
											}
									}
									break;
								}

							// Zeilenende
							case 0x0F:
								{
									// Kein weiterer while-Schleifen-Durchlauf => nächste Zeile (for-Schleife)
									Weiter = false;

									// Binary-Wert erstellen
									cmdEOL(ref aktFID);

									break;
								}
						} // Ende switch: Kommando auswerten
					} // Ende while: Kommando für Kommando

					// Rechtes RowEdge einfügen (leere Bereiche) Leere Bereiche sind transparent, also erstmal -1 als Palettenindex schreiben
					for(int k = 0; k < aktFID.RowEdge[j, 1]; k++)
					{
						aktFID.KommandoTabelle[j, aktPtr + k] = -1;
					}
					aktPtr += aktFID.RowEdge[j, 1];
				} // Ende for: Zeile für Zeile

				#endregion Kommandotabelle

				// Framedaten zur zentralen Liste hinzufügen
				_frameInformationenDaten.Add(aktFID);
			} // Ende for: Frame für Frame

			// Einstellungen speichern
			_settings = (useTransp ? Settings.UseTransparency : 0)
				| (useOutline1 ? Settings.UseOutline1 : 0)
				| (useOutline2 ? Settings.UseOutline2 : 0)
				| (usePlayerColor ? Settings.UsePlayerColor : 0)
				| (useShadow ? Settings.UseShadow : 0);

			#endregion Frame-Daten (RowEdge und Kommandotabelle)
		}

		/// <summary>
		/// Speichert die SLP-Daten im Puffer.
		/// </summary>
		/// <remarks></remarks>
		public void writeData()
		{
			// Zurücksetzen des Puffers
			_dataBuffer.Clear();

			// Header
			WriteString(_headers.Version, 4);
			WriteUInteger((uint)_frameInformationenHeaders.Count());
			WriteString(_headers.Kommentar, 24);

			#region Berechnungen zu den einzelnen Frames

			// Anzahl der Frames ermitteln
			uint anzahlFrames = (uint)_frameInformationenHeaders.Count();

			// Offset der ersten RowEdge-Definition (SLP-Header: 32 Byte; Frame-Header: 32 Byte pro Frame)
			uint pointer = 32 + 32 * anzahlFrames;

			// Offsets berechnen
			for(int i = 0; i < anzahlFrames; i++)
			{
				// Aktuelle Frameheader abrufen
				Strukturen.FrameInformationenHeader aktFIH = _frameInformationenHeaders[i];

				// Umriss-Offset speichern
				aktFIH.FrameUmrissOffset = pointer;

				// Die Offset-Daten sind jeweils Höhe * 4 Byte lang
				pointer += aktFIH.Höhe * 4;

				// Hier sind dann die Kommando-Offsets definiert
				aktFIH.FrameKommandoOffset = pointer;

				// Die Kommando-Offsets sind ebenfalls Höhe * 4 Byte lang
				pointer += aktFIH.Höhe * 4;

				// Berechnung der Kommando-Offsets
				Strukturen.FrameInformationenDaten aktFID = _frameInformationenDaten[i];

				// Offsets der Kommandotabelle berechnen
				for(int j = 0; j < aktFIH.Höhe; j++)
				{
					// Kommando-Offset der aktuellen Zeile
					aktFID.KommandoTabelleOffsets[j] = pointer;

					// Länge der Kommandobytes zur aktuellen Pointerposition addieren, um das nächste Offset angeben zu können
					pointer += (uint)cmdByteLengthInLine(ref aktFID, j);
				}

				// FIH und FID speichern
				_frameInformationenHeaders[i] = aktFIH;
				_frameInformationenDaten[i] = aktFID;
			}

			#endregion Berechnungen zu den einzelnen Frames

			// Frame-Informationen: Header

			#region Frameheader schreiben

			for(int i = 0; i < anzahlFrames; i++)
			{
				// Aktuelle Frameheader abrufen
				Strukturen.FrameInformationenHeader aktFIH = _frameInformationenHeaders[i];

				// Das Paletten-Offset ist 0
				aktFIH.PaletteOffset = 0;

				// Die Eigenschaften sind immer 24 (?)
				aktFIH.Eigenschaften = 24;

				// Werte schreiben
				WriteUInteger(aktFIH.FrameKommandoOffset);
				WriteUInteger(aktFIH.FrameUmrissOffset);
				WriteUInteger(aktFIH.PaletteOffset);
				WriteUInteger(aktFIH.Eigenschaften);
				WriteUInteger(aktFIH.Breite);
				WriteUInteger(aktFIH.Höhe);
				WriteInteger(aktFIH.XAnker);
				WriteInteger(aktFIH.YAnker);
			}

			#endregion Frameheader schreiben

			// Frameinformationen: Daten

			#region Framedaten schreiben

			for(int i = 0; i < _headers.Frameanzahl; i++)
			{
				Strukturen.FrameInformationenHeader aktFIH = _frameInformationenHeaders[i];
				Strukturen.FrameInformationenDaten aktFID = _frameInformationenDaten[i];

				// RowEdge
				for(int j = 0; j < aktFIH.Höhe; j++)
				{
					WriteUShort((ushort)aktFID.BinaryRowEdge[j]._left);
					WriteUShort((ushort)aktFID.BinaryRowEdge[j]._right);
				}

				// Kommando-Tabelle-Offsets
				for(int j = 0; j < aktFIH.Höhe; j++)
				{
					WriteUInteger(aktFID.KommandoTabelleOffsets[j]);
				}

				// Kommando-Tabelle
				int anzCommands = aktFID.BinaryCommandTable.Count;
				for(int j = 0; j < anzCommands; j++)
				{
					// Aktuelles Kommando abrufen
					binaryCommand aktC = aktFID.BinaryCommandTable[j];

					// Nach Typen unterscheidend die jeweiligen Daten schreiben
					if(aktC._type == "one")
					{
						// Kommando-Byte schreiben
						WriteByte(aktC._cmdbyte);
					}
					else if(aktC._type == "two length")
					{
						// Kommando-Byte schreiben
						WriteByte(aktC._cmdbyte);

						// Längenangabe schreiben
						WriteByte(aktC._nextByte);
					}
					else if(aktC._type == "two data")
					{
						// Kommando-Byte schreiben
						WriteByte(aktC._cmdbyte);

						// Daten schreiben
						WriteBytes(aktC._data);
					}
					else if(aktC._type == "three")
					{
						// Kommando-Byte schreiben
						WriteByte(aktC._cmdbyte);

						// Längenangabe schreiben
						WriteByte(aktC._nextByte);

						// Daten schreiben
						WriteBytes(aktC._data);
					}
				}
			}

			#endregion Framedaten schreiben
		}

		/// <summary>
		/// Gibt den angegebenen Frame als Bitmap-Bild zurück.
		/// </summary>
		/// <param name="frameID">Die ID des Frames.</param>
		/// <param name="Pal">Die zu verwendende Farbpalette als Palette-Objekt.</param>
		/// <param name="mask">Optional. Gibt die abzurufende Maske an; Standardwert ist die reine Frame-Grafik.</param>
		/// <param name="maskReplacementColor">Optional. Gibt die Farbe an, die anstatt der Masken verwendet werden soll, die nicht angezeigt werden.</param>
		/// <remarks></remarks>
		public Bitmap getFrameAsBitmap(uint frameID, ColorTable Pal, Masks mask = Masks.Graphic, Color? maskReplacementColor = null)
		{
			// Framedaten abrufen
			Strukturen.FrameInformationenHeader FIH = _frameInformationenHeaders[(int)frameID];

			// Rückgabebild erstellen
			Bitmap ret = new Bitmap((int)FIH.Breite, (int)FIH.Höhe);

			// Welche Maske ist gewollt?
			if(mask == Masks.Graphic) // Es handelt sich um die reine Frame-Grafik
			{
				// Bild pixelweise durchgehen
				for(int i = 0; i < FIH.Breite; i++)
				{
					for(int j = 0; j < FIH.Höhe; j++)
					{
						// Palettenindex abrufen
						int farbID = _frameInformationenDaten[(int)frameID].KommandoTabelle[j, i];

						// Sonderindizes in die jeweiligen Farben umsetzen
						Color col;
						if(maskReplacementColor == null)
							maskReplacementColor = Color.White;
						switch(farbID)
						{
							case -1:
							case -2:
							case -3:
								col = maskReplacementColor ?? Color.White;
								break;

							case -4:
								col = Pal[_schatten];
								break;

							default:
								col = Pal[farbID];
								break;
						}

						// Pixel in das Bild schreiben
						ret.SetPixel(i, j, col);
					}
				}
			}
			else if(mask != Masks.PlayerColor) // Es handelt sich um eine Maske (außer der Spielerfarbe)
			{
				// Den Index und die Zielfarbe angeben
				int maskIndex = 0;
				int maskColor = 0;
				if(mask == Masks.Transparency)
				{
					maskIndex = -1;
					maskColor = 0;
				}
				else if(mask == Masks.Outline1)
				{
					maskIndex = -2;
					maskColor = 8;
				}
				else if(mask == Masks.Outline2)
				{
					maskIndex = -3;
					maskColor = 124;
				}
				else if(mask == Masks.Shadow)
				{
					maskIndex = -4;
					maskColor = 131;
				}

				// Bild pixelweise durchgehen
				for(int i = 0; i < FIH.Breite; i++)
				{
					for(int j = 0; j < FIH.Höhe; j++)
					{
						// Palettenindex abrufen
						int farbID = _frameInformationenDaten[(int)frameID].KommandoTabelle[j, i];

						// Je nach Masken Farben setzen
						if(farbID == maskIndex)
						{
							// Masken-Farbe übernehmen
							farbID = maskColor;
						}
						else
						{
							// Keine Maske, also weiß
							farbID = 255;
						}

						// Pixel übernehmen
						ret.SetPixel(i, j, Pal[farbID]);
					}
				}
			}
			else // Spielerfarbe
			{
				// Bild pixelweise durchgehen
				for(int i = 0; i < FIH.Breite; i++)
				{
					for(int j = 0; j < FIH.Höhe; j++)
					{
						// Palettenindex abrufen
						int farbID = _frameInformationenDaten[(int)frameID].KommandoTabelle[j, i];

						// Liegt keine Spielerfarbe vor?
						if(farbID < 16 || farbID > 23)
						{
							// Pixel weiß einfärben
							farbID = 255;
						}

						// Pixel übernehmen
						ret.SetPixel(i, j, Pal[farbID]);
					}
				}
			}

			// Fertig
			return ret;
		}

		/// <summary>
		/// Gibt den angegebenen Frame als Farbarray zurück.
		/// </summary>
		/// <param name="frameID">Die ID des Frames.</param>
		/// <param name="Pal">Die zu verwendende Farbpalette als Palette-Objekt.</param>
		/// <param name="mask">Optional. Gibt die abzurufende Maske an; Standardwert ist die reine Frame-Grafik.</param>
		/// <remarks></remarks>
		public Color[,] getFrameAsColorArray(uint frameID, ColorTable Pal, Masks mask = Masks.Graphic)
		{
			// Framedaten abrufen
			Strukturen.FrameInformationenHeader FIH = _frameInformationenHeaders[(int)frameID];

			// Rückgabebild erstellen
			Color[,] ret = new Color[FIH.Breite, FIH.Höhe];

			// Welche Maske ist gewollt?
			if(mask == Masks.Graphic) // Es handelt sich um die reine Frame-Grafik
			{
				// Bild pixelweise durchgehen
				for(int i = 0; i < FIH.Breite; i++)
				{
					for(int j = 0; j < FIH.Höhe; j++)
					{
						// Palettenindex abrufen
						int farbID = _frameInformationenDaten[(int)frameID].KommandoTabelle[j, i];

						// Sonderindizes in die jeweiligen Farben umsetzen; meist Rein-Weiß
						switch(farbID)
						{
							case -1:
								farbID = 255;
								break;

							case -2:
								farbID = 255;
								break;

							case -3:
								farbID = 255;
								break;

							case -4:
								farbID = _schatten;
								break;
						}

						// Pixel in das Bild schreiben
						ret[i, j] = (farbID == 255 ? Color.FromArgb(0, 255, 255, 255) : Pal[farbID]); // 255er-Weiß gilt als transparent (Alpha = 0)
					}
				}
			}
			else if(mask != Masks.PlayerColor) // Es handelt sich um eine Maske (außer der Spielerfarbe)
			{
				// Den Index und die Zielfarbe angeben
				int maskIndex = 0;
				int maskColor = 0;
				if(mask == Masks.Transparency)
				{
					maskIndex = -1;
					maskColor = 0;
				}
				else if(mask == Masks.Outline1)
				{
					maskIndex = -2;
					maskColor = 8;
				}
				else if(mask == Masks.Outline2)
				{
					maskIndex = -3;
					maskColor = 124;
				}
				else if(mask == Masks.Shadow)
				{
					maskIndex = -4;
					maskColor = 131;
				}

				// Bild pixelweise durchgehen
				for(int i = 0; i < FIH.Breite; i++)
				{
					for(int j = 0; j < FIH.Höhe; j++)
					{
						// Palettenindex abrufen
						int farbID = _frameInformationenDaten[(int)frameID].KommandoTabelle[j, i];

						// Je nach Masken Farben setzen
						if(farbID == maskIndex)
						{
							// Masken-Farbe übernehmen
							farbID = maskColor;
						}
						else
						{
							// Keine Maske, also weiß
							farbID = 255;
						}

						// Pixel übernehmen
						ret[i, j] = Pal[farbID];
					}
				}
			}
			else // Spielerfarbe
			{
				// Bild pixelweise durchgehen
				for(int i = 0; i < FIH.Breite; i++)
				{
					for(int j = 0; j < FIH.Höhe; j++)
					{
						// Palettenindex abrufen
						int farbID = _frameInformationenDaten[(int)frameID].KommandoTabelle[j, i];

						// Liegt keine Spielerfarbe vor?
						if(farbID < 16 || farbID > 23)
						{
							// Pixel weiß einfärben
							farbID = 255;
						}

						// Pixel übernehmen
						ret[i, j] = Pal[farbID];
					}
				}
			}

			// Fertig
			return ret;
		}

		/// <summary>
		/// Ersetzt einen vorhandenen Frame oder fügt einen neuen am Ende hinzu.
		/// </summary>
		/// <param name="frameID">Die ID des Frames (bei Ersetzung) oder -1 für einen neuen Frame.</param>
		/// <param name="frameBitmap">Die Bilddaten, die in Kommando-Daten umgewandelt werden sollen (mit 50500er-Palette versehen).</param>
		/// <param name="pal">Die zu verwendende Farbpalette.</param>
		/// <param name="ankerX">Die X-Koordinate des Mittelpunkts der Grafik.</param>
		/// <param name="ankerY">Die Y-Koordinate des Mittelpunkts der Grafik.</param>
		/// <param name="settings">Die Einstellungen als Wert der Settings-Enumeration.</param>
		public void addReplaceFrame(int frameID, BitmapLoader frameBitmap, ColorTable pal, int ankerX, int ankerY, Settings settings)
		{
			#region Grundlegenes und Initialisierungen

			// Größen ermitteln
			int height = frameBitmap.Height;
			int width = frameBitmap.Width;

			// Framedaten
			Strukturen.FrameInformationenDaten aktFID;
			Strukturen.FrameInformationenHeader aktFIH;

			// Neuer Frame?
			if(frameID < 0 || frameID >= _frameInformationenDaten.Count)
			{
				// Neue Framedaten erstellen
				aktFID = new Strukturen.FrameInformationenDaten();
				_frameInformationenDaten.Add(aktFID);
				aktFIH = new Strukturen.FrameInformationenHeader();
				_frameInformationenHeaders.Add(aktFIH);

				// Neue Frame-ID ermitteln
				frameID = _frameInformationenDaten.Count - 1;
			}
			else
			{
				// Frame laden
				aktFID = _frameInformationenDaten[frameID];
				aktFIH = _frameInformationenHeaders[frameID];
			}

			// Anker speichern
			aktFIH.XAnker = ankerX;
			aktFIH.YAnker = ankerY;

			// Speichern der Abmessungen
			aktFIH.Breite = (uint)width;
			aktFIH.Höhe = (uint)height;

			// RowEdge initialisieren
			aktFID.BinaryRowEdge = new binaryRowedge[height];
			for(int i = 0; i < height; i++)
			{
				aktFID.BinaryRowEdge[i] = new binaryRowedge();
			}

			// Kommando-Offset-Array initalisieren
			aktFID.KommandoTabelleOffsets = new uint[height];

			// Kommando-Array initialisieren
			aktFID.BinaryCommandTable = new List<binaryCommand>();

			#endregion Grundlegenes und Initialisierungen

			// Bild in umgesetzte Kommandodaten umwandeln

			#region Umwandlung in umgesetzte Kommandodaten (Masken + Farbenindizes)

			int[,] xCommandTable; // [Y, X] => Hilfsvariable für effizienteren Zugriff, enthält alle Palettenverweise des Bilds bzw. die negativen Masken-Pixel
			aktFID.KommandoTabelle = new int[height, width];
			{
				// Vordefinition, um Zeit zu sparen
				Color aktC; // Bildpixel
				Color aktPC; // Palettenfarbe
				double temp; // Abstand
				double distance; // Größtmöglicher Farbabstand / aktuell nächster Farbabstand

				// Bildindizes (Daten) in die umgesetzte Kommandotabelle schreiben
				for(int i = 0; i < height; i++)
				{
					for(int j = 0; j < width; j++)
					{
						aktFID.KommandoTabelle[i, j] = frameBitmap[j, i];
					}
				}

				// Aus Effizienzgründen die umgewandelte Kommandotabelle in ein nahezu Objekt-freies int-Array kopieren (Direktzugriff ohne Umwege über unnötige OOP)
				xCommandTable = (int[,])aktFID.KommandoTabelle.Clone();

				// Berechnen der Masken

				#region Maskenberechnung

				{
					// Unsichtbar wird immer 255er-Weiß
					int _transMaskIndex = 255;

					// Transparenz
					if((settings & Settings.UseTransparency) == Settings.UseTransparency)
					{
						// Jedem transparenten 255er-Pixel den Index -1 verpassen
						for(int y = 0; y < height; y++)
						{
							for(int x = 0; x < width; x++)
							{
								if(xCommandTable[y, x] == _transMaskIndex)
									xCommandTable[y, x] = -1;
							}
						}
					}

					// Sämtliche Transparenz hat nun den Wert -1
					_transMaskIndex = -1;

					// Umriss 1 (Outline 1): Der Umriss des Objekts in der Grafik (wenn das Objekt hinter einem anderen steht, werden diese Umrisse in der Spielerfarbe dargestellt) Es müssen alle vier Richtungen durchgerechnet werden
					if((settings & Settings.UseOutline1) == Settings.UseOutline1)
					{
						// Waagerecht
						for(int y = 0; y < height; y++)
						{
							// Nach rechts
							for(int x = 0; x < width - 1; x++)
							{
								// Befindet sich beim nächsten Pixel keine Transparenz und auch kein Umriss 1? => Umriss
								if(xCommandTable[y, x] == _transMaskIndex && xCommandTable[y, x + 1] != _transMaskIndex && xCommandTable[y, x + 1] != -2)
								{
									xCommandTable[y, x] = -2;
								}
							}

							// Nach links
							for(int x = width - 1; x > 0; x--)
							{
								// Befindet sich beim nächsten Pixel keine Transparenz und auch kein Umriss 1? => Umriss
								if(xCommandTable[y, x] == _transMaskIndex && xCommandTable[y, x - 1] != _transMaskIndex && xCommandTable[y, x - 1] != -2)
								{
									xCommandTable[y, x] = -2;
								}
							}
						}

						// Senkrecht
						for(int x = 0; x < width; x++)
						{
							// Nach unten
							for(int y = 0; y < height - 1; y++)
							{
								// Befindet sich beim nächsten Pixel keine Transparenz und auch kein Umriss 1? => Umriss
								if(xCommandTable[y, x] == _transMaskIndex && xCommandTable[y + 1, x] != _transMaskIndex && xCommandTable[y + 1, x] != -2)
								{
									xCommandTable[y, x] = -2;
								}
							}

							// Nach oben
							for(int y = height - 1; y > 0; y--)
							{
								// Befindet sich beim nächsten Pixel keine Transparenz und auch kein Umriss 1? => Umriss
								if(xCommandTable[y, x] == _transMaskIndex && xCommandTable[y - 1, x] != _transMaskIndex && xCommandTable[y - 1, x] != -2)
								{
									xCommandTable[y, x] = -2;
								}
							}
						}
					}

					// Umriss 2 (Outline 2): Zweiter Umriss, umschließt Umriss 1
					if((settings & Settings.UseOutline2) == Settings.UseOutline2)
					{
						// Zuerst Outline 1 für das Outline 2-Array neu berechnen (falls dies noch nicht passiert ist)
						if((settings & Settings.UseOutline1) != Settings.UseOutline1)
						{
							// Waagerecht
							for(int y = 0; y < height; y++)
							{
								// Nach rechts
								for(int x = 0; x < width - 1; x++)
								{
									// Befindet sich beim nächsten Pixel keine Transparenz und auch kein Umriss 1? => Umriss
									if(xCommandTable[y, x] == _transMaskIndex && xCommandTable[y, x + 1] != _transMaskIndex && xCommandTable[y, x + 1] != -2)
									{
										xCommandTable[y, x] = -2;
									}
								}

								// Nach links
								for(int x = width - 1; x > 0; x--)
								{
									// Befindet sich beim nächsten Pixel keine Transparenz und auch kein Umriss 1? => Umriss
									if(xCommandTable[y, x] == _transMaskIndex && xCommandTable[y, x - 1] != _transMaskIndex && xCommandTable[y, x - 1] != -2)
									{
										xCommandTable[y, x] = -2;
									}
								}
							}

							// Senkrecht
							for(int x = 0; x < width; x++)
							{
								// Nach unten
								for(int y = 0; y < height - 1; y++)
								{
									// Befindet sich beim nächsten Pixel keine Transparenz und auch kein Umriss 1? => Umriss
									if(xCommandTable[y, x] == _transMaskIndex && xCommandTable[y + 1, x] != _transMaskIndex && xCommandTable[y + 1, x] != -2)
									{
										xCommandTable[y, x] = -2;
									}
								}

								// Nach oben
								for(int y = height - 1; y > 0; y--)
								{
									// Befindet sich beim nächsten Pixel keine Transparenz und auch kein Umriss 1? => Umriss
									if(xCommandTable[y, x] == _transMaskIndex && xCommandTable[y - 1, x] != _transMaskIndex && xCommandTable[y - 1, x] != -2)
									{
										xCommandTable[y, x] = -2;
									}
								}
							}
						}

						// Outline 2 auf Outline 1 basierend erstellen (Umriss 1 wird wie eine gewöhnliche Farbe behandelt)
						{
							// Waagerecht
							for(int y = 0; y < height; y++)
							{
								// Nach rechts
								for(int x = 0; x < width - 1; x++)
								{
									// Befindet sich beim nächsten Pixel keine Transparenz? => Umriss
									if(xCommandTable[y, x] == _transMaskIndex && xCommandTable[y, x + 1] != _transMaskIndex && (x + 1 != width - 1))
									{
										xCommandTable[y, x] = -3;
									}
								}

								// Nach links
								for(int x = width - 1; x > 0; x--)
								{
									// Befindet sich beim nächsten Pixel keine Transparenz? => Umriss
									if(xCommandTable[y, x] == _transMaskIndex && xCommandTable[y, x - 1] != _transMaskIndex && (x + 1 != width - 1))
									{
										xCommandTable[y, x] = -3;
									}
								}
							}

							// Senkrecht
							for(int x = 0; x < width; x++)
							{
								// Nach unten
								for(int y = 0; y < height - 1; y++)
								{
									// Befindet sich beim nächsten Pixel keine Transparenz? => Umriss
									if(xCommandTable[y, x] == _transMaskIndex && xCommandTable[y + 1, x] != _transMaskIndex)
									{
										xCommandTable[y, x] = -3;
									}
								}

								// Nach oben
								for(int y = height - 1; y > 0; y--)
								{
									// Befindet sich beim nächsten Pixel keine Transparenz? => Umriss
									if(xCommandTable[y, x] == _transMaskIndex && xCommandTable[y - 1, x] != _transMaskIndex)
									{
										xCommandTable[y, x] = -3;
									}
								}
							}
						}
					}

					// Schatten (Farbindex 131)
					if((settings & Settings.UseShadow) == Settings.UseShadow)
					{
						// Jeden Schattenpixel mit -4 markieren
						for(int y = 0; y < height; y++)
						{
							for(int x = 0; x < width; x++)
							{
								if(xCommandTable[y, x] == 131)
									xCommandTable[y, x] = -4;
							}
						}
					}
				} // Ende Maskenberechnung

				#endregion Maskenberechnung

				// Kommandotabelle zwischenspeichern
				aktFID.KommandoTabelle = (int[,])xCommandTable.Clone();
			} // Ende Umwandlung in umgesetzte Kommandodaten

			#endregion Umwandlung in umgesetzte Kommandodaten (Masken + Farbenindizes)

			// Generieren der RowEdge-Daten (Außenränder, Bestimmung waagerecht)

			#region Generierung der RowEdge-Daten

			for(int y = 0; y < height; y++)
			{
				// Von links nach rechts
				int left = 0;
				for(int x = 0; x < width; x++)
				{
					if(xCommandTable[y, x] != -1)
						break;
					else
						left++;
				}

				// Von rechts nach links
				int right = 0;
				for(int x = width - 1; x >= 0; x--)
				{
					if(xCommandTable[y, x] != -1)
						break;
					else
						right++;
				}

				// Liegt eine leere Zeile vor?
				if(left == width)
				{
					// Leere Zeile
					right = 0;
				}

				// Werte speichern
				aktFID.BinaryRowEdge[y] = new binaryRowedge(left, right);
			}

			#endregion Generierung der RowEdge-Daten

			// Ziel-Kommando-Tabelle erstellen

			#region Erstellung der binären Ziel-Kommandotabelle

			{
				// Zeilenweise vorgehen
				for(int y = 0; y < height; y++)
				{
					// Der vorherige Pixel
					int prev = -5; // -5 ist ein Wert, den kein Pixel annehmen kann (nur -4 bis 255)

					// Bereich festlegen, in dem sich überhaupt etwas anderes als Transparenz befindet (vom Bildrand ausgehend)
					int start = aktFID.BinaryRowEdge[y]._left;
					int end = width - aktFID.BinaryRowEdge[y]._right - 1;

					// Aktuelle Position (Beginn dort, wo sich keine Transparenz mehr befindet)
					int pos = start;

					// Anzahl der Blöcke vom gleichen Typ
					int typeCount = 0;

					// Der aktuelle Typ
					string type = "null";

					// Positionsvariablen für Farbblöcke
					int colorStart = -1;
					int colorLength = -1;

					// Alle Pixel bis zum Bereichsende durchgehen
					while(pos <= end)
					{
						// Der aktuelle Pixel
						int currentPixel = xCommandTable[y, pos];

						// Liegt keine Farbe/Spielerfarbe mehr vor und beginnt ein Masken-Block (negativer Wert) bzw. beginnt ein anderer Masken-Block als der aktuell laufende?
						if(currentPixel != prev && currentPixel < 0)
						{
							// Wenn aktuell eine Maske läuft, diese in die Kommandotabelle übernehmen
							if(type != "null" && type != "color")
							{
								if(type == "transp")
									cmdTransp(ref aktFID, typeCount);
								else if(type == "outline1")
									cmdOutline1(ref aktFID, typeCount);
								else if(type == "outline2")
									cmdOutline2(ref aktFID, typeCount);
								else if(type == "shadow")
									cmdShadow(ref aktFID, typeCount);
							}
							else if(type == "color")
							{
								// Farben in Byte-Array schreiben
								byte[] colors = new byte[colorLength];
								for(int i = 0; i < colorLength; i++)
								{
									colors[i] = (byte)xCommandTable[y, colorStart + i];
								}

								// Farben in Kommandotabelle übernehmen
								cmdColorBlock(ref aktFID, colors, (settings & Settings.UsePlayerColor) == Settings.UsePlayerColor);

								// Der Farbblock ist zuende
								colorStart = -1;
								colorLength = -1;
							}

							// Neuen Typen erstellen
							typeCount = 1;
							if(currentPixel == -1)
								type = "transp";
							else if(currentPixel == -2)
								type = "outline1";
							else if(currentPixel == -3)
								type = "outline2";
							else if(currentPixel == -4)
								type = "shadow";

							// Aktuellen Pixelwert speichern, um ihn beim nächsten Durchlauf wieder verwenden zu können
							prev = currentPixel;
						}
						else if(currentPixel != prev && prev < 0) // Wechsel von Maske zu Farbe
						{
							// Den vorherigen Block schreiben
							if(type == "transp")
								cmdTransp(ref aktFID, typeCount);
							else if(type == "outline1")
								cmdOutline1(ref aktFID, typeCount);
							else if(type == "outline2")
								cmdOutline2(ref aktFID, typeCount);
							else if(type == "shadow")
								cmdShadow(ref aktFID, typeCount);

							// Neuen Typen erstellen
							typeCount = 1;
							type = "color";

							// Farbblockeigenschaften festlegen
							colorStart = pos;
							colorLength = 1;

							// Aktuellen Pixelwert speichern, um ihn beim nächsten Durchlauf wieder verwenden zu können
							prev = currentPixel;
						}
						else if(type == "color") // Farbblock geht weiter
						{
							// Farbblock verlängern (egal, welche Farbe)
							colorLength++;
							typeCount = 1;

							// Aktuellen Pixel speichern
							prev = currentPixel;
						}
						else // Maske geht weiter
						{
							// Anzahl erhöhen, der letzte Pixel ist natürlich der gleiche wie der aktuelle
							typeCount++;
						}

						// Nächster Pixel
						pos++;
					}

					// Letzten offenen Block noch beenden
					if(type == "color") // Farbblock
					{
						// Farben in Byte-Array schreiben
						byte[] colors = new byte[colorLength];
						for(int i = 0; i < colorLength; i++)
						{
							colors[i] = (byte)xCommandTable[y, colorStart + i];
						}

						// Farben in Kommandotabelle übernehmen
						cmdColorBlock(ref aktFID, colors, (settings & Settings.UsePlayerColor) == Settings.UsePlayerColor);

						// Der Farbblock ist zuende
						colorStart = -1;
						colorLength = -1;
					}
					else // Maske
					{
						// Den Maskenblock schreiben
						if(type == "transp")
							cmdTransp(ref aktFID, typeCount);
						else if(type == "outline1")
							cmdOutline1(ref aktFID, typeCount);
						else if(type == "outline2")
							cmdOutline2(ref aktFID, typeCount);
						else if(type == "shadow")
							cmdShadow(ref aktFID, typeCount);
					}

					// Zeilenende schreiben
					cmdEOL(ref aktFID);
				}
			}

			#endregion Erstellung der binären Ziel-Kommandotabelle

			// Frame-Daten-Variablen speichern
			_frameInformationenHeaders[frameID] = aktFIH;
			_frameInformationenDaten[frameID] = aktFID;

			// Sicherheitshalber Frame-Anzahl im Header aktualisieren
			_headers.Frameanzahl = (uint)_frameInformationenHeaders.Count;

			// Fertig: RowEdge-Daten und Kommandotabelle vollständig erstellt.
		}

		/// <summary>
		/// Exportiert den angegebenen Frame in eine Bitmap-Datei (50500er-Palette).
		/// </summary>
		/// <param name="frameID">Die ID des zu exportierenden Frame.</param>
		/// <param name="filename">Die Bitmap-Datei, in die die Daten geschrieben werden sollen.</param>
		/// <param name="mask">Die zu exportierende Maske (oder reine Grafik) als Element der Masks-Enumeration.</param>
		public void exportFrame(int frameID, string filename, Masks mask = Masks.Graphic)
		{
			// Framedaten abrufen
			Strukturen.FrameInformationenHeader FIH = _frameInformationenHeaders[(int)frameID];

			// Rückgabebild erstellen
			BitmapLoader bmp = new BitmapLoader((int)FIH.Breite, (int)FIH.Höhe);

			// Welche Maske ist gewollt?
			if(mask == Masks.Graphic) // Es handelt sich um die reine Frame-Grafik
			{
				// Bild pixelweise durchgehen
				for(int i = 0; i < FIH.Breite; i++)
				{
					for(int j = 0; j < FIH.Höhe; j++)
					{
						// Palettenindex abrufen
						int farbID = _frameInformationenDaten[(int)frameID].KommandoTabelle[j, i];

						// Sonderindizes in die jeweiligen Farben umsetzen; meist Rein-Weiß
						switch(farbID)
						{
							case -1:
								farbID = 255;
								break;

							case -2:
								farbID = 255;
								break;

							case -3:
								farbID = 255;
								break;

							case -4:
								farbID = _schatten;
								break;
						}

						// Pixel in das Bild schreiben
						bmp[i, j] = (byte)farbID;
					}
				}
			}
			else if(mask != Masks.PlayerColor) // Es handelt sich um eine Maske (außer der Spielerfarbe)
			{
				// Den Index und die Zielfarbe angeben
				int maskIndex = 0;
				int maskColor = 0;
				if(mask == Masks.Transparency)
				{
					maskIndex = -1;
					maskColor = 0;
				}
				else if(mask == Masks.Outline1)
				{
					maskIndex = -2;
					maskColor = 8;
				}
				else if(mask == Masks.Outline2)
				{
					maskIndex = -3;
					maskColor = 124;
				}
				else if(mask == Masks.Shadow)
				{
					maskIndex = -4;
					maskColor = 131;
				}

				// Bild pixelweise durchgehen
				for(int i = 0; i < FIH.Breite; i++)
				{
					for(int j = 0; j < FIH.Höhe; j++)
					{
						// Palettenindex abrufen
						int farbID = _frameInformationenDaten[(int)frameID].KommandoTabelle[j, i];

						// Je nach Masken Farben setzen
						if(farbID == maskIndex)
						{
							// Masken-Farbe übernehmen
							farbID = maskColor;
						}
						else
						{
							// Keine Maske, also weiß
							farbID = 255;
						}

						// Pixel übernehmen
						bmp[i, j] = (byte)farbID;
					}
				}
			}
			else // Spielerfarbe
			{
				// Bild pixelweise durchgehen
				for(int i = 0; i < FIH.Breite; i++)
				{
					for(int j = 0; j < FIH.Höhe; j++)
					{
						// Palettenindex abrufen
						int farbID = _frameInformationenDaten[(int)frameID].KommandoTabelle[j, i];

						// Liegt keine Spielerfarbe vor?
						if(farbID < 16 || farbID > 23)
						{
							// Pixel weiß einfärben
							farbID = 255;
						}

						// Pixel übernehmen
						bmp[i, j] = (byte)farbID;
					}
				}
			}

			// Fertig, Bild speichern
			bmp.saveToFile(filename);
		}

		#endregion Funktionen

		#region Eigenschaften

		/// <summary>
		/// Gibt die Anzahl der Frames zurück.
		/// </summary>
		/// <value></value>
		/// <returns></returns>
		/// <remarks></remarks>
		public uint FrameCount
		{
			get
			{
				return _headers.Frameanzahl;
			}
		}

		#endregion Eigenschaften

		#region Hilfsfunktionen

		// Die folgenden Funktionen sind Abkürzungen, in C++ wären dies Makros.

		#region Lesen

		/// <summary>
		/// Gibt genau ein Byte aus DataBuffer zurück.
		/// </summary>
		/// <returns></returns>
		/// <remarks></remarks>
		private byte ReadByte()
		{
			return _dataBuffer.ReadByte();
		}

		/// <summary>
		/// Gibt ein Byte-Array aus DataBuffer zurück.
		/// </summary>
		/// <param name="count">Die Anzahl der auszulesenden Bytes.</param>
		/// <returns></returns>
		/// <remarks></remarks>
		private byte[] ReadBytes(int count)
		{
			return _dataBuffer.ReadByteArray(count);
		}

		/// <summary>
		/// Gibt genau einen UShort-Wert aus DataBuffer zurück.
		/// </summary>
		/// <returns></returns>
		/// <remarks></remarks>
		private ushort ReadUShort()
		{
			return _dataBuffer.ReadUShort();
		}

		/// <summary>
		/// Gibt genau einen Integer-Wert aus DataBuffer zurück.
		/// </summary>
		/// <returns></returns>
		/// <remarks></remarks>
		private int ReadInteger()
		{
			return _dataBuffer.ReadInteger();
		}

		/// <summary>
		/// Gibt genau einen UInteger-Wert aus DataBuffer zurück.
		/// </summary>
		/// <returns></returns>
		/// <remarks></remarks>
		private uint ReadUInteger()
		{
			return _dataBuffer.ReadUInteger();
		}

		#endregion Lesen

		#region Schreiben

		/// <summary>
		/// Schreibt ein Byte an das Ende des Puffers.
		/// </summary>
		/// <param name="value">Das zu schreibende Byte.</param>
		/// <remarks></remarks>
		private void WriteByte(byte value)
		{
			_dataBuffer.WriteByte(value);
		}

		/// <summary>
		/// Schreibt ein Byte-Array an das Ende des Puffers.
		/// </summary>
		/// <param name="value">Das zu schreibende Byte-Array.</param>
		/// <remarks></remarks>
		private void WriteBytes(byte[] value)
		{
			_dataBuffer.Write(value);
		}

		/// <summary>
		/// Schreibt einen UShort-Wert an das Ende des Puffers.
		/// </summary>
		/// <param name="value">Der zu schreibende Wert.</param>
		/// <remarks></remarks>
		private void WriteUShort(ushort value)
		{
			_dataBuffer.WriteUShort(value);
		}

		/// <summary>
		/// Schreibt einen Integer-Wert an das Ende des Puffers.
		/// </summary>
		/// <param name="value">Der zu schreibende Wert.</param>
		/// <remarks></remarks>
		private void WriteInteger(int value)
		{
			_dataBuffer.WriteInteger(value);
		}

		/// <summary>
		/// Schreibt einen UInteger-Wert an das Ende des Puffers.
		/// </summary>
		/// <param name="value">Der zu schreibende Wert.</param>
		/// <remarks></remarks>
		private void WriteUInteger(uint value)
		{
			_dataBuffer.WriteUInteger(value);
		}

		/// <summary>
		/// Schreibt eine Zeichenfolge in den Puffer und hängt eine binäre Null dahinter, falls dort keine stehen sollte.
		/// </summary>
		/// <param name="value">Die zu schreibende Zeichenfolge.</param>
		/// <remarks></remarks>
		private void WriteString(string value)
		{
			WriteString(value, 0);
		}

		/// <summary>
		/// Schreibt eine Zeichenfolge in den Puffer und hängt eine binäre Null dahinter, falls dort keine stehen sollte.
		/// </summary>
		/// <param name="value">Die zu schreibende Zeichenfolge.</param>
		/// <param name="SollLänge">Legt die Länge des Strings fest. Nicht belegte Zeichen werden mit 0-Bytes ergänzt.</param>
		/// <remarks></remarks>
		private void WriteString(string value, int sollLänge)
		{
			// Byte-Array mit der gewünschten Stringlänge erstellen
			byte[] zuSchreiben = new byte[sollLänge];

			// Zeichenfolge in Byte-Array schreiben
			zuSchreiben = System.Text.Encoding.Default.GetBytes(value);

			// In Puffer schreiben
			_dataBuffer.Write(zuSchreiben);
		}

		#endregion Schreiben

		#region Erstellung der binären Kommandotabelle

		private void cmdTransp(ref Strukturen.FrameInformationenDaten fid, int length)
		{
			// Bei Längen >= 64 das Array aufspalten
			if(length >= 64)
			{
				// Array links und rechts halbieren und Funktion erneut aufrufen (rekursiv) Längen der beiden Seiten (Mittelpunkt suchen)
				int left, right;

				// Mittelpunkt suchen, nach ungeraden und geraden Längen unterscheiden
				if(length % 2 == 0) // Gerade
				{
					// Beide Seiten sind gleich lang
					left = length / 2;
					right = length / 2;
				}
				else
				{
					// Links ist kürzer als rechts
					left = (length - 1) / 2;
					right = (length + 1) / 2;
				}

				// Neuaufruf
				cmdTransp(ref fid, left);
				cmdTransp(ref fid, right);

				// Fertig
				return;
			}

			// Das Kommandobyte
			byte command = (byte)(length << 2);

			// Wert des Kommandobytes um 1 erhöhen, da sonst Zweideutigkeit mit den Farbkommandos entsteht (ebenfalls 0, 4, 8, 12...)
			int commandNibbleLeft = (int)(command & 0x0C);
			if(commandNibbleLeft == 0 || commandNibbleLeft == 4 || commandNibbleLeft == 8 || commandNibbleLeft == 12)
			{
				command = (byte)(((int)command & 0xFF) + 1); // "& 0xFF", um Überläufe zu verhindern
			}

			// Kommando anfügen
			fid.BinaryCommandTable.Add(new binaryCommand(command));
		}

		private void cmdColor(ref Strukturen.FrameInformationenDaten fid, byte[] array)
		{
			// Array-Länge bestimmen
			int length = array.Length;

			// Bei Längen >= 64 das Array aufspalten
			if(length >= 64)
			{
				// Array links und rechts halbieren und Funktion erneut aufrufen (rekursiv) Längen der beiden Seiten (Mittelpunkt suchen)
				int left, right;

				// Mittelpunkt suchen, nach ungeraden und geraden Längen unterscheiden
				if(length % 2 == 0) // Gerade
				{
					// Beide Seiten sind gleich lang
					left = length / 2;
					right = length / 2;
				}
				else
				{
					// Links ist kürzer als rechts
					left = (length - 1) / 2;
					right = (length + 1) / 2;
				}

				// Die beiden Arrays initialisieren
				byte[] aLeft = new byte[left];
				byte[] aRight = new byte[right];

				// Die Werte verschieben
				for(int i = 0; i < left; i++)
				{
					aLeft[i] = array[i];
				}
				for(int i = 0; i < right; i++)
				{
					aRight[i] = array[left + i];
				}

				// Rekursiver Neu-Aufruf mit den beiden neuen Arrays
				cmdColor(ref fid, aLeft);
				cmdColor(ref fid, aRight);

				// Ende
				return;
			}

			// Akzeptable Länge, Kommando-Byte erstellen
			byte command = (byte)(length << 2);

			// Kommando der Auflistung hinzufügen
			fid.BinaryCommandTable.Add(new binaryCommand(command, array));
		}

		private void cmdPlayerColor(ref Strukturen.FrameInformationenDaten fid, byte[] array)
		{
			// Das Kommandobyte ist schon bekannt
			byte command = 0x06;

			// Array-Länge bestimmen
			int length = array.Length;

			// Zweitarray (Zielarray) erstellen
			byte[] array2 = new byte[length];

			// Bei zu großer Array-Länge die zu schreibenden Daten rekursiv aufteilen
			if(length >= 256)
			{
				// Array links und rechts halbieren und Funktion erneut aufrufen (rekursiv) Längen der beiden Seiten (Mittelpunkt suchen)
				int left, right;

				// Mittelpunkt suchen, nach ungeraden und geraden Längen unterscheiden
				if(length % 2 == 0) // Gerade
				{
					// Beide Seiten sind gleich lang
					left = length / 2;
					right = length / 2;
				}
				else
				{
					// Links ist kürzer als rechts
					left = (length - 1) / 2;
					right = (length + 1) / 2;
				}

				// Die beiden Arrays initialisieren
				byte[] aLeft = new byte[left];
				byte[] aRight = new byte[right];

				// Die Werte verschieben
				for(int i = 0; i < left; i++)
				{
					aLeft[i] = array[i];
				}
				for(int i = 0; i < right; i++)
				{
					aRight[i] = array[left + i];
				}

				// Rekursiver Neu-Aufruf mit den beiden neuen Arrays
				cmdPlayerColor(ref fid, aLeft);
				cmdPlayerColor(ref fid, aRight);

				// Ende
				return;
			}

			// Das nächste Byte definieren (unnötige Variable, eher der Übersichtlichkeit halber)
			byte nextByte = (byte)length;

			// Ergebnisarray erstellen
			for(int i = 0; i < length; i++)
			{
				array2[i] = (byte)(array[i] - 16);
			}

			// Kommando erstellen
			fid.BinaryCommandTable.Add(new binaryCommand(command, nextByte, array2));
		}

		private void cmdColorBlock(ref Strukturen.FrameInformationenDaten fid, byte[] array, bool usePlayerColor)
		{
			// Länge der Daten
			int aLength = array.Length;

			// Positions- und Zustandsvariablen erstellen
			int start = 0;
			int length = 0;
			int playerColorStart = 0;
			int playerColorLength = 0; // Gibt an, wie lange schon eine Spielerfarbe vorliegt
			bool isPrevPlayerColor = false;

			// Aktuelle Position
			int pos = 0;

			// Prüfen, ob schon am Anfang eine Spielerfarbe vorliegt
			isPrevPlayerColor = ((array[pos] >= 16 && array[pos] <= 23) && usePlayerColor);
			if(isPrevPlayerColor)
			{
				playerColorStart = 0;
				playerColorLength = 1;
			}
			else
			{
				start = 0;
				length = 1;
			}

			// Byte für Byte das Array durchgehen
			pos = 1;
			while(pos < aLength)
			{
				// Liegt eine Spielerfarbe vor?
				bool isPlayerColor = ((array[pos] >= 16 && array[pos] <= 23) && usePlayerColor);
				if(isPlayerColor && !isPrevPlayerColor)
				{
					// Es beginnt eine neue Spielerfarbe, also den vorherigen Farbblock abschließen und schreiben
					byte[] toWrite = new byte[length];
					for(int i = 0; i < length; i++)
					{
						toWrite[i] = array[start + i];
					}
					cmdColor(ref fid, toWrite);

					// Neuen Spielerfarben-Block anfangen
					playerColorStart = pos;
					playerColorLength = 1;

					// Es liegt eine Spielerfarbe vor
					isPrevPlayerColor = true;
				}
				else if(!isPlayerColor && isPrevPlayerColor)
				{
					// Es endet ein Spielerfarben-Block, also muss dieser geschrieben werden
					byte[] toWrite = new byte[playerColorLength];
					for(int i = 0; i < playerColorLength; i++)
					{
						toWrite[i] = array[playerColorStart + i];
					}
					cmdPlayerColor(ref fid, toWrite);

					// Neuen Farbblock beginnen
					start = pos;
					length = 1;

					// Es liegt keine Spielerfarbe mehr vor
					isPrevPlayerColor = false;
				}
				else if(isPlayerColor)
				{
					// Der laufende Spielerfarbenblock geht weiter
					playerColorLength++;
				}
				else
				{
					// Der laufende Farbblock geht weiter
					length++;
				}

				// Nächster Index
				pos++;
			}

			// Den letzten Block noch abschließen
			if(isPrevPlayerColor)
			{
				// Spielerfarbe
				byte[] toWrite = new byte[playerColorLength];
				for(int i = 0; i < playerColorLength; i++)
				{
					toWrite[i] = array[playerColorStart + i];
				}
				cmdPlayerColor(ref fid, toWrite);
			}
			else
			{
				// Normale Farbe
				byte[] toWrite = new byte[length];
				for(int i = 0; i < length; i++)
				{
					toWrite[i] = array[start + i];
				}
				cmdColor(ref fid, toWrite);
			}
		}

		private void cmdOutline1(ref Strukturen.FrameInformationenDaten fid, int length)
		{
			// Das Kommandobyte
			byte command;

			// Anhand der Länge entscheiden, ob ein Block vorliegt oder nicht
			if(length == 1)
			{
				// Einfacher Umriss-Pixel
				command = 0x4E;
				fid.BinaryCommandTable.Add(new binaryCommand(command));
			}
			else
			{
				// Block
				command = 0x5E;
				byte nextByte = (byte)length;
				fid.BinaryCommandTable.Add(new binaryCommand(command, nextByte));
			}
		}

		private void cmdOutline2(ref Strukturen.FrameInformationenDaten fid, int length)
		{
			// Das Kommandobyte
			byte command;

			// Anhand der Länge entscheiden, ob ein Block vorliegt oder nicht
			if(length == 1)
			{
				// Einfacher Umriss-Pixel
				command = 0x6E;
				fid.BinaryCommandTable.Add(new binaryCommand(command));
			}
			else
			{
				// Block
				command = 0x7E;
				byte nextByte = (byte)length;
				fid.BinaryCommandTable.Add(new binaryCommand(command, nextByte));
			}
		}

		private void cmdShadow(ref Strukturen.FrameInformationenDaten fid, int length)
		{
			// Das Kommandobyte
			byte command = 0x0B;

			// Zu lange Schatten-Blöcke in rekursiv behandelte Teilstücke zerlegen
			if(length >= 256)
			{
				// Array links und rechts halbieren und Funktion erneut aufrufen (rekursiv) Längen der beiden Seiten (Mittelpunkt suchen)
				int left, right;

				// Mittelpunkt suchen, nach ungeraden und geraden Längen unterscheiden
				if(length % 2 == 0) // Gerade
				{
					// Beide Seiten sind gleich lang
					left = length / 2;
					right = length / 2;
				}
				else
				{
					// Links ist kürzer als rechts
					left = (length - 1) / 2;
					right = (length + 1) / 2;
				}

				// Neuaufruf
				cmdShadow(ref fid, left);
				cmdShadow(ref fid, right);

				// Fertig
				return;
			}

			// Das nächste Byte kommt in...
			byte nextByte = (byte)length;

			// Kommando anfügen
			fid.BinaryCommandTable.Add(new binaryCommand(command, nextByte));
		}

		private void cmdEOL(ref Strukturen.FrameInformationenDaten fid)
		{
			// Das Kommandobyte
			byte command = 0x0F;

			// Kommando anfügen
			fid.BinaryCommandTable.Add(new binaryCommand(command));
		}

		#endregion Erstellung der binären Kommandotabelle

		#region Berechnungen auf der binären Kommandotabelle

		private void computeCmdBytes(ref Strukturen.FrameInformationenDaten fid, ref int destCommandFullLength)
		{
			// Gesamtzahl der Kommandos und die Gesamt-Kommandolänge berechnen
			int total = 0;
			for(int i = 0; i < fid.BinaryCommandTable.Count; i++)
			{
				total += fid.BinaryCommandTable[i].CommandLength;
			}

			// Wert über Referenz zurück an die aufrufende Funktion übergeben
			destCommandFullLength = total;
		}

		private int cmdByteLengthInLine(ref Strukturen.FrameInformationenDaten fid, int line)
		{
			// Positionsvariablen
			int aktLine = 0;
			int aktLength = 0;

			// Alle Kommandos durchlaufen
			for(int i = 0; i < fid.BinaryCommandTable.Count; i++)
			{
				// Kommandolänge hinzuaddieren
				aktLength += fid.BinaryCommandTable[i].CommandLength;

				// Wurde ein Zeilenende erreicht?
				if(fid.BinaryCommandTable[i]._cmdbyte == 0x0F)
				{
					// Liegt die gesuchte Zeile vor?
					if(aktLine == line)
					{
						// Zeilenlänge zurückgeben
						return aktLength;
					}
					else
					{
						// Nächste Zeile
						aktLine++;
						aktLength = 0;
					}
				}
			}

			// Zeile nicht gefunden?
			return 0;
		}

		#endregion Berechnungen auf der binären Kommandotabelle

		#endregion Hilfsfunktionen

		#region Hilfsstrukturen und Hilfsklassen

		/// <summary>
		/// Repräsentiert eine Umriss-Eigenschaft.
		/// </summary>
		public class binaryRowedge
		{
			public int _left;
			public int _right;

			public binaryRowedge()
			{
				_left = 0;
				_right = 0;
			}

			public binaryRowedge(int l, int r)
			{
				_left = l;
				_right = r;
			}
		}

		/// <summary>
		/// Repräsentiert ein Kommando.
		/// </summary>
		public class binaryCommand
		{
			/// <summary>
			/// Das Kommando-Byte.
			/// </summary>
			public byte _cmdbyte;

			/// <summary>
			/// Das nächste Byte.
			/// </summary>
			public byte _nextByte;

			/// <summary>
			/// Die beschriebenen Daten.
			/// </summary>
			public byte[] _data;

			/// <summary>
			/// Hilfsvariable zum Speichern, welche Aktion vorliegt (Anzahl der Konstruktor-Argumente).
			/// </summary>
			public String _type;

			#region Konstruktoren

			/// <summary>
			/// Definiert ein Kommando ohne Daten.
			/// </summary>
			/// <param name="b">Das Kommando-Byte.</param>
			public binaryCommand(byte b)
			{
				_cmdbyte = b;
				_nextByte = 0;
				_data = new byte[0];
				_type = "one";
			}

			/// <summary>
			/// Definiert ein Kommando ohne Daten, aber mit nachfolgendem Byte.
			/// </summary>
			/// <param name="b">Das Kommando-Byte.</param>
			/// <param name="n">Das nachfolgende Byte.</param>
			public binaryCommand(byte b, byte n)
			{
				_cmdbyte = b;
				_nextByte = n;
				_data = new byte[0];
				_type = "two length";
			}

			/// <summary>
			/// Definiert ein Kommando mit Daten.
			/// </summary>
			/// <param name="b">Das Kommando-Byte.</param>
			/// <param name="d">Die Daten.</param>
			public binaryCommand(byte b, byte[] d)
			{
				_cmdbyte = b;
				_nextByte = 0;
				_data = d;
				_type = "two data";
			}

			/// <summary>
			/// Definiert ein Kommando mit Daten und mit nachfolgendem Byte.
			/// </summary>
			/// <param name="b">Das Kommando-Byte.</param>
			/// <param name="n">Das nachfolgende Byte.</param>
			/// <param name="d">Die Daten.</param>
			public binaryCommand(byte b, byte n, byte[] d)
			{
				_cmdbyte = b;
				_nextByte = n;
				_data = d;
				_type = "three";
			}

			#endregion Konstruktoren

			/// <summary>
			/// Ruft die Gesamtlänge des Kommandos ab.
			/// </summary>
			public int CommandLength
			{
				get
				{
					// Fallunterscheidung, je nach Kommando-Typ
					switch(_type)
					{
						case "one":
							return 1;

						case "two length":
							return 2;

						case "two data":
							return 1 + _data.Length;

						case "three":
							return 2 + _data.Length;

						default:
							return 0; // Merkwürdig...
					}
				}
			}
		}

		#endregion Hilfsstrukturen und Hilfsklassen

		#region Enumerationen

		/// <summary>
		/// Definiert die Einstellungen bei der Erstellung der Frame-Kommandotabelle.
		/// </summary>
		[Flags]
		public enum Settings : short
		{
			UseTransparency = 1,
			UseOutline1 = 2,
			UseOutline2 = 4,
			UsePlayerColor = 8,
			UseShadow = 16
		}

		/// <summary>
		/// Definiert eine Maske.
		/// </summary>
		public enum Masks : short
		{
			Graphic = 0,
			Transparency = 1,
			Outline1 = 2,
			Outline2 = 3,
			PlayerColor = 4,
			Shadow = 5
		}

		#endregion Enumerationen
	}
}
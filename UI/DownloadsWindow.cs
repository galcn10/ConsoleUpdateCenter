using ConsoleUpdateCenter.Core;
using ConsoleUpdateCenter.Providers;
using ConsoleUpdateCenter.Providers.Steam;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace ConsoleUpdateCenter.UI
{
    /// <summary>Juego seleccionado en Playnite al abrir la pantalla (para la ficha "Este juego").</summary>
    public class GameFocus
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public Guid PluginId { get; set; }
        public string GameId { get; set; }
        public bool IsInstalled { get; set; }
        public ImageSource Cover { get; set; }
        public ImageSource Backdrop { get; set; }
    }

    /// <summary>Lo que la pantalla necesita del plugin.</summary>
    internal sealed class DownloadsActions
    {
        public Action UpdateAll;
        public Action<UpdatePlatform> CheckPlatform;
        public Action<string, string, string> Toast;
        public Func<bool> TranslateNotes;
    }

    /// <summary>
    /// Pantalla "Descargas" con el aspecto de Console Overlay.
    /// </summary>
    internal sealed class DownloadsWindow : ConsoleScreen
    {
        private enum Mode
        {
            Main,
            Notes,
            ConfirmKeepUpdated,
            Launchers
        }

        private readonly UpdateService service;
        private readonly DownloadsActions actions;
        private readonly GameFocus focusGame;
        private Mode mode = Mode.Main;
        private string notesText;
        private string notesNote;
        private bool firstBuild = true;
        private string progressText;
        private string infoText;
        private DateTime lastRebuild;
        private bool rebuildQueued;

        public static DownloadsWindow Open { get; private set; }

        public DownloadsWindow(Look look, UpdateService service, DownloadsActions actions, GameFocus focusGame) : base(look)
        {
            this.service = service;
            this.actions = actions;
            this.focusGame = focusGame;
            Title = "Console Update Center";

            Loaded += (_, __) =>
            {
                Open = this;
                service.Changed += OnServiceChanged;
                Rebuild();
                Log.Debug("Pantalla Descargas abierta" + (focusGame != null ? " con " + focusGame.Name : "") + ".");
            };
            Closed += (_, __) =>
            {
                service.Changed -= OnServiceChanged;
                if (Open == this)
                {
                    Open = null;
                }
            };
        }

        public void SetProgressText(string text)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                progressText = text;
                Rebuild();
            }));
        }

        private void OnServiceChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (mode == Mode.Main)
                {
                    Rebuild();
                }
            }));
        }

        /// <summary>Reconstruye como mucho cada 400 ms para no parpadear con ráfagas de cambios.</summary>
        private void Rebuild()
        {
            var wait = 400 - (DateTime.Now - lastRebuild).TotalMilliseconds;
            if (wait > 0 && !firstBuild)
            {
                if (!rebuildQueued)
                {
                    rebuildQueued = true;
                    Task.Delay((int)wait).ContinueWith(_ => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        rebuildQueued = false;
                        Rebuild();
                    })));
                }

                return;
            }

            lastRebuild = DateTime.Now;
            switch (mode)
            {
                case Mode.Notes:
                    SetPage(BuildNotesPage());
                    break;
                case Mode.ConfirmKeepUpdated:
                    SetPage(BuildConfirmPage());
                    break;
                case Mode.Launchers:
                    SetPage(BuildLaunchersPage());
                    break;
                default:
                    SetPage(BuildMainPage());
                    break;
            }

            firstBuild = false;
        }

        private void Go(Mode m)
        {
            mode = m;
            firstBuild = true;
            Rebuild();
        }

        #region Página principal

        private ScreenPage BuildMainPage()
        {
            var entries = service.Entries;
            var providers = service.ActiveProviders;
            var page = new ScreenPage { Title = Loc.T("Descargas"), BigIcon = "download" };
            FillSummary(page, entries);

            var focusEntry = focusGame != null ? entries.FirstOrDefault(e => e.PlayniteId == focusGame.Id) : null;
            var focusProvider = focusGame != null ? providers.FirstOrDefault(p => p.LibraryPluginId == focusGame.PluginId) : null;

            // Este juego: su estado y una fila de iconos con sus acciones
            if (focusGame != null)
            {
                page.Groups.Add(new ScreenGroup
                {
                    Title = Loc.T("Este juego") + " · " + focusGame.Name,
                    Rows = { GameStateRow(focusEntry, focusProvider) }
                });

                var gameActions = GameActions(focusProvider);
                if (gameActions.Count > 0)
                {
                    var g = new ScreenGroup { Horizontal = true };
                    g.Rows.AddRange(gameActions);
                    page.Groups.Add(g);
                }
            }

            // Opciones generales
            var options = new ScreenGroup { Title = Loc.T("Opciones"), Horizontal = true };
            var checkable = providers.Where(p => p.CanForceCheck).Select(p => p.DisplayName).ToList();
            options.Rows.Add(new ScreenRow
            {
                Key = "updateall",
                Icon = "refresh-cw",
                Text = service.IsUpdatingAll ? Loc.T("Comprobando…") : Loc.T("Buscar en todo"),
                Desc = checkable.Count > 0
                    ? Loc.T("Reinicia {0} en silencio para que comprueben y descarguen", string.Join(", ", checkable))
                    : Loc.T("No hay launchers que se puedan comprobar"),
                Enabled = !service.IsUpdatingAll && checkable.Count > 0,
                Execute = actions.UpdateAll
            });

            var armed = service.ArmedPower;
            options.Rows.Add(new ScreenRow
            {
                Key = "power",
                Icon = armed == PowerAction.Sleep ? "moon" : "power",
                Color = armed == PowerAction.None ? (Color?)null : Look.ActionColor(armed == PowerAction.Sleep ? "moon" : "power"),
                Text = armed == PowerAction.Shutdown ? Loc.T("Al terminar: apagar")
                    : armed == PowerAction.Sleep ? Loc.T("Al terminar: suspender")
                    : Loc.T("Al terminar: nada"),
                Desc = infoText ?? Loc.T("{A} cambia: no hacer nada → apagar → suspender"),
                Execute = CyclePower
            });

            var lo = service.LaunchOnlyGames;
            if (lo.Count > 0)
            {
                options.Rows.Add(new ScreenRow
                {
                    Key = "keepupdated",
                    Icon = "wrench",
                    Text = Loc.T("Ajustes de Steam"),
                    Desc = Loc.T("Mantener actualizados {0} juego(s)", lo.Count) + ". " + Loc.T("Ahora solo se actualizan al iniciarlos"),
                    Enabled = !service.IsUpdatingAll,
                    Execute = () => Go(Mode.ConfirmKeepUpdated)
                });
            }

            options.Rows.Add(new ScreenRow
            {
                Key = "launchers",
                Icon = "external-link",
                Color = Look.PlatformColor(UpdatePlatform.Steam),
                Text = Loc.T("Launchers"),
                Desc = string.Join(" · ", providers.Select(p => p.DisplayName)),
                Enabled = providers.Count > 0,
                Execute = () => Go(Mode.Launchers)
            });
            page.Groups.Add(options);

            // Descargas
            var others = entries.Where(e => focusGame == null || e.PlayniteId != focusGame.Id)
                .OrderBy(e => e.Status == UpdateStatus.Downloading ? 0 : e.Status == UpdateStatus.Paused ? 1 : e.Status == UpdateStatus.Pending ? 2 : 3)
                .ThenBy(e => e.Name)
                .ToList();

            var list = new ScreenGroup { Title = focusGame != null ? Loc.T("Otras descargas") : Loc.T("Descargas") };
            if (others.Count == 0)
            {
                list.Rows.Add(new ScreenRow
                {
                    Key = "uptodate",
                    Icon = "circle-check",
                    Text = service.LastScan == null ? Loc.T("Escaneando…") : Loc.T("Todo está actualizado"),
                    Enabled = false
                });
            }

            foreach (var e in others)
            {
                list.Rows.Add(GameRow(e, providers.FirstOrDefault(p => p.Platform == e.Platform)));
            }

            page.Groups.Add(list);

            page.AddHint("A", Loc.T("Seleccionar"));
            page.AddHint("B", Loc.T("Cerrar"));
            page.AddHint("X", Loc.T("Escanear"));
            page.OnX = () => { Task.Run(() => service.ScanAsync(UpdateService.ScanReason.Manual)); };
            page.OnY = () => actions.UpdateAll?.Invoke();

            if (firstBuild)
            {
                page.FocusKey = focusGame != null
                    ? page.Groups.SelectMany(g => g.Rows).FirstOrDefault(r => r.Enabled && r.Key.StartsWith("this|"))?.Key
                    : others.Count > 0 ? "game|" + others[0].Key : "updateall";
            }

            return page;
        }

        private void FillSummary(ScreenPage page, IReadOnlyList<UpdateEntry> entries)
        {
            var pending = entries.Count(e => e.Status != UpdateStatus.LaunchOnly);
            var downloading = entries.Where(e => e.Status == UpdateStatus.Downloading).ToList();
            page.AddChip(pending == 0 ? Loc.T("Al día") : Loc.T("{0} pendiente(s)", pending), pending == 0 ? Look.OkBrush : null);
            if (downloading.Count > 0)
            {
                page.AddChip(Loc.T("{0} descargando", downloading.Count));
                var speed = Format.Speed(downloading.Sum(e => e.BytesPerSecond));
                if (speed != null)
                {
                    page.AddChip(speed);
                }
            }

            if (entries.Any(e => e.NoSpace))
            {
                page.AddChip(Loc.T("Sin espacio en disco"), Look.DangerBrush);
            }

            if (service.ArmedPower != PowerAction.None)
            {
                page.AddChip(service.ArmedPower == PowerAction.Sleep ? Loc.T("Al terminar: suspender") : Loc.T("Al terminar: apagar"), Look.MidBrush);
            }

            page.Message = service.IsUpdatingAll
                ? progressText
                : service.LastScan.HasValue ? Loc.T("Último escaneo {0}", service.LastScan.Value.ToShortTimeString()) : Loc.T("Escaneando…");
        }

        #endregion

        #region Este juego

        /// <summary>Fila con el estado del juego (progreso si se está actualizando).</summary>
        private ScreenRow GameStateRow(UpdateEntry entry, ILauncherProvider provider)
        {
            if (entry != null)
            {
                var row = GameRow(entry, provider);
                row.Key = "this|game";
                row.AlwaysDesc = true;
                if (entry.NoSpace)
                {
                    row.Desc = Loc.T("No cabe en {0}: faltan {1} y hay {2} libres.",
                        Format.Drive(entry.LibraryFolder), Format.Bytes(entry.BytesLeft), Format.Bytes(entry.FreeBytes));
                }
                else if (entry.Status == UpdateStatus.LaunchOnly)
                {
                    row.Desc = Loc.T("Steam solo actualiza este juego al iniciarlo.");
                }

                return row;
            }

            string text, desc = null, icon = "circle-check";
            Color? color = Look.ActionColor("check");
            if (provider == null)
            {
                text = Loc.T("Sin opciones de actualización");
                desc = Loc.T("Esta plataforma no tiene actualizaciones gestionables desde aquí.");
                icon = "info";
                color = null;
            }
            else if (!focusGame.IsInstalled)
            {
                text = Loc.T("No está instalado");
                icon = "info";
                color = null;
            }
            else if (!provider.CanDetect)
            {
                text = provider.DisplayName;
                desc = Loc.T("{0} no informa del estado: revísalo en el launcher.", provider.DisplayName);
                icon = "info";
                color = null;
            }
            else
            {
                text = service.LastScan == null ? Loc.T("Comprobando…") : Loc.T("Al día");
                desc = provider.DisplayName;
            }

            return new ScreenRow
            {
                Key = "this|state",
                Icon = icon,
                Color = color,
                Text = text,
                Desc = desc,
                AlwaysDesc = desc != null,
                Enabled = false
            };
        }

        /// <summary>Acciones del juego como iconos: buscar, novedades, mantener actualizado, validar y abrir el launcher.</summary>
        private List<ScreenRow> GameActions(ILauncherProvider provider)
        {
            var rows = new List<ScreenRow>();
            if (provider == null || !focusGame.IsInstalled)
            {
                return rows;
            }

            var platform = provider.Platform;
            var isSteam = platform == UpdatePlatform.Steam && !string.IsNullOrEmpty(focusGame.GameId);

            if (provider.CanForceCheck)
            {
                rows.Add(new ScreenRow
                {
                    Key = "this|check",
                    Icon = "refresh-cw",
                    Text = service.IsUpdatingAll ? Loc.T("Comprobando…") : Loc.T("Buscar actualización"),
                    Desc = Loc.T("Reinicia {0} en silencio para que compruebe y descargue", provider.DisplayName),
                    Enabled = !service.IsUpdatingAll,
                    Execute = () => actions.CheckPlatform?.Invoke(platform)
                });
            }

            if (isSteam)
            {
                var appId = focusGame.GameId;
                rows.Add(new ScreenRow
                {
                    Key = "this|notes",
                    Icon = "file-text",
                    Text = Loc.T("Novedades del parche"),
                    Desc = Loc.T("Notas de las últimas actualizaciones publicadas en Steam"),
                    Execute = ShowNotes
                });

                if (service.IsLaunchOnly(appId))
                {
                    rows.Add(new ScreenRow
                    {
                        Key = "this|keep",
                        Icon = "wrench",
                        Text = Loc.T("Mantener este juego actualizado"),
                        Desc = Loc.T("Ahora Steam solo lo actualiza al iniciarlo"),
                        Enabled = !service.IsUpdatingAll,
                        Execute = () => KeepUpdated(service.LaunchOnlyGames.Where(g => g.AppId == appId).ToList())
                    });
                }

                rows.Add(new ScreenRow
                {
                    Key = "this|validate",
                    Icon = "shield-check",
                    Text = Loc.T("Validar archivos"),
                    Desc = Loc.T("Steam revisa el juego y descarga lo que falte (más lento)"),
                    Execute = () => SteamProvider.Validate(appId)
                });
            }

            rows.Add(new ScreenRow
            {
                Key = "this|open",
                Icon = platform == UpdatePlatform.Xbox ? "store" : "external-link",
                Color = Look.PlatformColor(platform),
                Text = provider.Platform == UpdatePlatform.Xbox ? "Microsoft Store" : provider.DisplayName,
                Desc = OpenLabel(provider) + ". " + Loc.T("Abre el launcher para revisarlo a mano"),
                Execute = provider.OpenUpdatesPage
            });

            return rows;
        }

        #endregion

        #region Launchers

        private ScreenPage BuildLaunchersPage()
        {
            var page = new ScreenPage
            {
                Title = Loc.T("Launchers"),
                BigIcon = "external-link",
                BigIconColor = Look.PlatformColor(UpdatePlatform.Steam),
                OnBack = () => Go(Mode.Main)
            };

            foreach (var p in service.ActiveProviders)
            {
                var provider = p;
                page.Rows.Add(new ScreenRow
                {
                    Key = "open|" + provider.Platform,
                    Icon = provider.Platform == UpdatePlatform.Xbox ? "store" : "external-link",
                    Color = Look.PlatformColor(provider.Platform),
                    Text = OpenLabel(provider),
                    Desc = provider.CanDetect ? Loc.T("Detección por juego disponible") : Loc.T("Sin detección por juego: se revisa en el propio launcher"),
                    Execute = provider.OpenUpdatesPage
                });
            }

            page.AddHint("A", Loc.T("Seleccionar"));
            page.AddHint("B", Loc.T("Volver"));
            return page;
        }

        #endregion

        #region Filas de juegos

        private ScreenRow GameRow(UpdateEntry e, ILauncherProvider provider)
        {
            var showBar = e.Status == UpdateStatus.Downloading || e.Status == UpdateStatus.Paused;
            return new ScreenRow
            {
                Key = "game|" + e.Key,
                Icon = Format.StatusIcon(e),
                Color = e.NoSpace ? Look.ActionColor("hard-drive") : Look.PlatformColor(e.Platform),
                Text = e.Name,
                Desc = Format.StatusLine(e),
                Right = showBar ? Format.Percent(e.Progress) : null,
                Progress = showBar ? e.Progress : null,
                Danger = e.NoSpace,
                Execute = provider == null ? (Action)null : provider.OpenUpdatesPage
            };
        }

        private static string OpenLabel(ILauncherProvider p)
        {
            switch (p.Platform)
            {
                case UpdatePlatform.Steam: return Loc.T("Abrir descargas de Steam");
                case UpdatePlatform.Xbox: return Loc.T("Actualizaciones de Microsoft Store");
                default: return Loc.T("Abrir {0}", p.DisplayName);
            }
        }

        #endregion

        #region Apagar al terminar

        private void CyclePower()
        {
            var next = service.ArmedPower == PowerAction.None ? PowerAction.Shutdown
                : service.ArmedPower == PowerAction.Shutdown ? PowerAction.Sleep
                : PowerAction.None;

            infoText = null;
            if (!service.ArmPower(next))
            {
                infoText = Loc.T("No hay descargas en curso");
            }

            Rebuild();
        }

        #endregion

        #region Novedades del parche

        private void ShowNotes()
        {
            notesText = Loc.T("Cargando…");
            notesNote = null;
            Go(Mode.Notes);

            var appId = focusGame.GameId;
            Task.Run(async () =>
            {
                string text;
                string note = null;
                try
                {
                    var translate = actions.TranslateNotes?.Invoke() ?? true;
                    var items = await SteamNews.GetAsync(appId, Loc.Current, translate).ConfigureAwait(false);
                    note = items.Any(i => i.Source == SteamNews.Source.Machine) ? Loc.T("Traducido automáticamente con Google Translate.")
                        : items.Any(i => i.Source == SteamNews.Source.TranslationFailed) ? Loc.T("No se pudo traducir; se muestra el texto original.")
                        : items.Any(i => i.Source == SteamNews.Source.SteamOfficial) ? Loc.T("Traducción oficial publicada en Steam.")
                        : null;
                    if (items.Count == 0)
                    {
                        text = Loc.T("Steam no tiene novedades publicadas para este juego.");
                    }
                    else
                    {
                        var sb = new StringBuilder();
                        foreach (var i in items)
                        {
                            sb.AppendLine(i.Title);
                            sb.AppendLine(i.Date.ToShortDateString());
                            sb.AppendLine();
                            sb.AppendLine(i.Text);
                            sb.AppendLine();
                            sb.AppendLine("— — —");
                            sb.AppendLine();
                        }

                        text = sb.ToString().TrimEnd('\r', '\n', ' ', '—');
                    }
                }
                catch (Exception e)
                {
                    Log.Warn("No se pudieron cargar las novedades de Steam.", e);
                    text = Loc.T("No se pudieron cargar las novedades. Comprueba la conexión.");
                }

                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    notesText = text;
                    notesNote = note;
                    if (mode == Mode.Notes)
                    {
                        firstBuild = true;
                        Rebuild();
                    }
                }));
            });
        }

        private ScreenPage BuildNotesPage()
        {
            var page = new ScreenPage
            {
                Cover = focusGame?.Cover,
                BigIcon = "file-text",
                Title = Loc.T("Novedades del parche"),
                LongText = notesText ?? string.Empty,
                LongTextNote = notesNote,
                Wide = true,
                OnBack = () => Go(Mode.Main)
            };
            page.AddChip(focusGame?.Name ?? "Steam");
            page.AddHint("B", Loc.T("Volver"));
            page.Message = Loc.T("Usa arriba y abajo para desplazarte.");
            return page;
        }

        #endregion

        #region Mantener actualizado (Steam)

        private ScreenPage BuildConfirmPage()
        {
            var games = service.LaunchOnlyGames;
            var page = new ScreenPage
            {
                BigIcon = "wrench",
                Title = Loc.T("Mantener actualizados {0} juego(s)", games.Count),
                Message = Loc.T("Steam se cerrará y volverá a abrirse. Se guarda una copia de seguridad de cada ajuste cambiado."),
                OnBack = () => Go(Mode.Main)
            };

            foreach (var g in games.Take(6))
            {
                page.AddChip(g.Name);
            }

            if (games.Count > 6)
            {
                page.AddChip(Loc.T("y {0} más", games.Count - 6));
            }

            page.Rows.Add(new ScreenRow
            {
                Key = "confirm",
                Icon = "check",
                Text = Loc.T("Sí, mantenerlos actualizados"),
                Desc = Loc.T("Pasan a «Mantener siempre actualizado»"),
                Execute = () => KeepUpdated(games.ToList())
            });
            page.Rows.Add(new ScreenRow
            {
                Key = "cancel",
                Icon = "x",
                Text = Loc.T("Cancelar"),
                Execute = () => Go(Mode.Main)
            });

            page.AddHint("A", Loc.T("Seleccionar"));
            page.AddHint("B", Loc.T("Volver"));
            page.FocusKey = "confirm";
            return page;
        }

        private void KeepUpdated(List<SteamProvider.LaunchOnlyGame> games)
        {
            if (games.Count == 0)
            {
                return;
            }

            Go(Mode.Main);
            Task.Run(async () =>
            {
                var changed = await service.KeepSteamGamesUpdatedAsync(games).ConfigureAwait(false);
                string title, sub;
                if (changed < 0)
                {
                    title = Loc.T("No se puede ahora");
                    sub = Loc.T("Cierra el juego abierto e inténtalo de nuevo.");
                }
                else
                {
                    title = Loc.T("Ajustes de Steam actualizados");
                    sub = Loc.T("{0} juego(s) se mantendrán actualizados.", changed);
                }

                actions.Toast?.Invoke(changed > 0 ? "circle-check" : "triangle-alert", title, sub);
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    Activate();
                    Rebuild();
                }));
            });
        }

        #endregion
    }
}

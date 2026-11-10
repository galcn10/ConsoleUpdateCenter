using ConsoleUpdateCenter.Core;
using System;

namespace ConsoleUpdateCenter.UI
{
    /// <summary>
    /// Cuenta atrás antes de apagar o suspender al terminar las descargas. Se puede cancelar con el mando.
    /// </summary>
    internal sealed class CountdownScreen : ConsoleScreen
    {
        private readonly PowerAction action;
        private readonly Action execute;
        private int secondsLeft;
        private bool done;

        public CountdownScreen(Look look, PowerAction action, int seconds, Action execute) : base(look)
        {
            this.action = action;
            this.execute = execute;
            secondsLeft = Math.Max(10, seconds);
            Title = "Console Update Center";
            Loaded += (_, __) => Build(true);
        }

        protected override void OnTick()
        {
            if (done)
            {
                return;
            }

            secondsLeft--;
            if (secondsLeft <= 0)
            {
                Run();
                return;
            }

            Build(false);
        }

        protected override void OnGuide() => Cancel();

        private void Build(bool first)
        {
            var sleep = action == PowerAction.Sleep;
            var page = new ScreenPage
            {
                BigIcon = sleep ? "moon" : "power",
                Title = Loc.T("Las descargas han terminado"),
                Message = sleep
                    ? Loc.T("El equipo se suspenderá en {0} s.", secondsLeft)
                    : Loc.T("El equipo se apagará en {0} s.", secondsLeft),
                OnBack = Cancel,
                FocusKey = first ? "cancel" : null
            };

            page.Rows.Add(new ScreenRow
            {
                Key = "now",
                Icon = sleep ? "moon" : "power",
                Text = sleep ? Loc.T("Suspender ahora") : Loc.T("Apagar ahora"),
                Execute = Run
            });
            page.Rows.Add(new ScreenRow
            {
                Key = "cancel",
                Icon = "x",
                Text = Loc.T("Cancelar"),
                Desc = Loc.T("El equipo seguirá encendido"),
                Execute = Cancel
            });

            page.AddHint("A", Loc.T("Seleccionar"));
            page.AddHint("B", Loc.T("Cancelar"));
            SetPage(page);
        }

        private void Run()
        {
            if (done)
            {
                return;
            }

            done = true;
            Log.Info("Ejecutando acción de energía: " + action);
            Close();
            try
            {
                execute?.Invoke();
            }
            catch (Exception e)
            {
                Log.Error("No se pudo ejecutar la acción de energía.", e);
            }
        }

        private void Cancel()
        {
            if (done)
            {
                return;
            }

            done = true;
            Log.Info("Acción de energía cancelada por el usuario.");
            Close();
        }
    }
}

using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using AlvMod.Plugin.Atualizacao;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(AlvMod.Plugin.App))]
[assembly: CommandClass(typeof(AlvMod.Plugin.Comandos))]

namespace AlvMod.Plugin
{
    /// <summary>
    /// Inicialização do plugin: ao abrir o AutoCAD, verifica em segundo plano se há versão nova no
    /// GitHub e, quando o AutoCAD fica ocioso, pergunta Sim/Não ao usuário.
    /// </summary>
    public sealed class App : IExtensionApplication
    {
        private static volatile ReleaseGitHub _pendente;
        private static volatile bool _verificacaoConcluida;

        public void Initialize()
        {
            try { Atualizador.LimparAntigos(); } catch { }

            // Carrega o núcleo agora: se o plugin for atualizado com o AutoCAD aberto, as duas partes antigas já
            // estão na memória e não se mistura versão nova com antiga ("Method not found") até reiniciar.
            _ = typeof(AlvMod.Core.Modulacao.Modulador).Assembly.FullName;

            Escrever($"\nAlvMod {Atualizador.VersaoAtualTexto} carregado. Comandos: ALVOBRA, ALVPLANTA, ALVPLANTA2, ALV1FIADA, ALV2FIADA, ALVQUANT, ALVRELATORIO, ALVCATALOGO, ALVATUALIZAR, ALVVERSAO.");

            if (Atualizador.PastaBundle == null) return; // carregado com NETLOAD (desenvolvimento)

            Task.Run(() =>
            {
                try { _pendente = Atualizador.BuscarAtualizacao(); }
                catch { /* sem internet / GitHub fora: tenta de novo na próxima abertura */ }
                finally { _verificacaoConcluida = true; }
            });
            AcApp.Idle += AoFicarOcioso;
        }

        public void Terminate() { }

        private static void AoFicarOcioso(object sender, EventArgs e)
        {
            if (!_verificacaoConcluida) return;
            AcApp.Idle -= AoFicarOcioso;

            var release = _pendente;
            if (release != null) PerguntarEAtualizar(release);
        }

        internal static void PerguntarEAtualizar(ReleaseGitHub release)
        {
            var notas = string.IsNullOrWhiteSpace(release.Notas) ? "" : "\n\nNovidades:\n" + Resumir(release.Notas);
            var resposta = MessageBox.Show(
                $"Há uma nova versão do AlvMod: {release.Tag} (instalada: {Atualizador.VersaoAtualTexto}).{notas}\n\nAtualizar agora?",
                "AlvMod — atualização disponível",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (resposta != DialogResult.Yes) return;

            try
            {
                Atualizador.Instalar(release);
                MessageBox.Show(
                    $"AlvMod atualizado para {release.Tag}.\n\nFeche e abra o AutoCAD para usar a nova versão.",
                    "AlvMod", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    "Não foi possível atualizar:\n" + ex.Message +
                    "\n\nFeche o AutoCAD e rode o instalar.ps1 de novo para instalar a versão nova.",
                    "AlvMod", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static string Resumir(string texto) => texto.Length <= 600 ? texto : texto.Substring(0, 600) + "…";

        internal static void Escrever(string msg)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            doc?.Editor.WriteMessage(msg);
        }
    }
}

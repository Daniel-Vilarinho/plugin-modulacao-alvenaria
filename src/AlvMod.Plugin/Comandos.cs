using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AlvMod.Core.Catalogo;
using AlvMod.Core.Modulacao;
using AlvMod.Plugin.Atualizacao;
using AlvMod.Plugin.Desenho;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AlvMod.Plugin
{
    public sealed class Comandos
    {
        private const string SufixoSegundaFiada = " 2F";

        // Lembrado durante a sessão do AutoCAD (o cabeçalho do DWG não é confiável — sempre perguntar).
        private static double _fatorCm = 1;

        [CommandMethod("ALV1FIADA")]
        public void PrimeiraFiada()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            if (!PerguntarFator(ed)) return;

            var familia = CatalogoPadrao.Familia39Concreto();
            var modulador = new Modulador(familia);

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var trechos = new List<Trecho>();
                while (true)
                {
                    var t = Geometria.SelecionarTrecho(ed, tr,
                        trechos.Count == 0 ? "\nSelecione o eixo da parede: " : "\nSelecione o próximo segmento: ");
                    if (t == null) break;
                    trechos.Add(t);
                    if (!PerguntarSimNao(ed, "\nHá outro segmento desta parede (cortada por porta)?")) break;
                }
                if (trechos.Count == 0) return;

                var desenhista = new Desenhista(doc.Database, tr, _fatorCm);
                int ok = 0;
                for (int i = 0; i < trechos.Count; i++)
                {
                    var r = modulador.PrimeiraFiada(trechos[i].Comprimento * _fatorCm);
                    if (!r.Sucesso)
                    {
                        ed.WriteMessage($"\n[INCONSISTÊNCIA] Trecho {i + 1}: {r.Mensagem}");
                        continue;
                    }
                    foreach (var p in r.Pecas) desenhista.Desenhar(trechos[i], p, "");
                    ed.WriteMessage($"\nTrecho {i + 1}: {r.Mensagem}");
                    ok++;
                }
                tr.Commit();
                Resumo(ed, ok, trechos.Count, desenhista);
            }
        }

        [CommandMethod("ALV2FIADA")]
        public void SegundaFiada()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            if (!PerguntarFator(ed)) return;

            var modulador = new Modulador(CatalogoPadrao.Familia39Concreto());

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var trecho = Geometria.SelecionarTrecho(ed, tr, "\nSelecione o eixo do trecho de parede: ");
                if (trecho == null) return;

                var filtro = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT,LWPOLYLINE") });
                var sel = ed.GetSelection(
                    new PromptSelectionOptions { MessageForAdding = "\nSelecione as peças da 1ª fiada deste trecho (já editadas): " },
                    filtro);
                if (sel.Status != PromptStatus.OK) return;

                double comprimento = trecho.Comprimento * _fatorCm;
                var intervalos = new List<(double ini, double fim)>();
                foreach (var id in sel.Value.GetObjectIds())
                {
                    var cantos = Geometria.Cantos((Entity)tr.GetObject(id, OpenMode.ForRead), tr);
                    if (cantos.Count == 0) continue;

                    var ts = new List<double>();
                    bool foraDaParede = false;
                    foreach (var c in cantos)
                    {
                        trecho.Projetar(c, out double t, out double d);
                        ts.Add(t * _fatorCm);
                        if (Math.Abs(d * _fatorCm) > 20) foraDaParede = true; // peça de outra parede
                    }
                    if (foraDaParede) continue;
                    intervalos.Add((Math.Max(0, ts.Min()), Math.Min(comprimento, ts.Max())));
                }

                intervalos = intervalos.Where(iv => iv.fim - iv.ini > 1).OrderBy(iv => iv.ini).ToList();
                if (intervalos.Count == 0)
                {
                    ed.WriteMessage("\nNenhuma peça da 1ª fiada encontrada sobre este eixo.");
                    return;
                }
                if (intervalos[0].ini > 1 || intervalos[intervalos.Count - 1].fim < comprimento - 1)
                    ed.WriteMessage("\n[AVISO] A 1ª fiada selecionada não cobre o trecho inteiro.");

                var juntas = new List<double>();
                for (int i = 0; i + 1 < intervalos.Count; i++)
                    juntas.Add((intervalos[i].fim + intervalos[i + 1].ini) / 2);

                var r = modulador.SegundaFiada(comprimento, juntas);
                if (!r.Sucesso)
                {
                    ed.WriteMessage($"\n[INCONSISTÊNCIA] {r.Mensagem}");
                    return;
                }

                var desenhista = new Desenhista(doc.Database, tr, _fatorCm);
                foreach (var p in r.Pecas) desenhista.Desenhar(trecho, p, SufixoSegundaFiada);
                tr.Commit();

                ed.WriteMessage($"\n{r.Mensagem}");
                ed.WriteMessage($"\n{juntas.Count} juntas lidas da 1ª fiada. 2ª fiada desenhada nos layers \"...{SufixoSegundaFiada}\".");
            }
        }

        [CommandMethod("ALVVERSAO")]
        public void Versao()
        {
            var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
            ed.WriteMessage($"\nAlvMod {Atualizador.VersaoAtualTexto} — repositório {Atualizador.Repositorio}" +
                            (Atualizador.PastaBundle == null ? " (carregado por NETLOAD)" : $"\nInstalado em {Atualizador.PastaBundle}"));
        }

        [CommandMethod("ALVATUALIZAR")]
        public void Atualizar()
        {
            var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
            ReleaseGitHub release;
            try
            {
                release = Atualizador.BuscarAtualizacao();
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nNão foi possível consultar o GitHub: " + ex.Message);
                return;
            }

            if (release == null)
            {
                ed.WriteMessage($"\nAlvMod {Atualizador.VersaoAtualTexto} já é a versão mais recente.");
                return;
            }
            App.PerguntarEAtualizar(release);
        }

        private static bool PerguntarFator(Editor ed)
        {
            var opcoes = new PromptDoubleOptions(
                "\nQuantos cm vale 1 unidade do desenho? (1 = cm, 100 = metro, 0.1 = mm)")
            {
                AllowNegative = false,
                AllowZero = false,
                DefaultValue = _fatorCm,
                UseDefaultValue = true
            };
            var res = ed.GetDouble(opcoes);
            if (res.Status != PromptStatus.OK) return false;
            _fatorCm = res.Value;
            return true;
        }

        private static bool PerguntarSimNao(Editor ed, string mensagem)
        {
            var opcoes = new PromptKeywordOptions(mensagem);
            opcoes.Keywords.Add("Sim");
            opcoes.Keywords.Add("Nao");
            opcoes.Keywords.Default = "Nao";
            opcoes.AllowNone = true;
            var res = ed.GetKeywords(opcoes);
            return res.Status == PromptStatus.OK && res.StringResult == "Sim";
        }

        private static void Resumo(Editor ed, int ok, int total, Desenhista d)
        {
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n{0} de {1} trecho(s) modulado(s), {2} peça(s) inserida(s).", ok, total, d.PecasInseridas));
            if (d.BlocosProvisorios.Count > 0)
                ed.WriteMessage("\n[AVISO] Blocos criados como retângulo provisório (não existiam no desenho): " +
                                string.Join(", ", d.BlocosProvisorios));
        }
    }
}

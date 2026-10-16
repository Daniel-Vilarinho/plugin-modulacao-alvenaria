using System;
using System.Collections.Generic;
using System.Linq;
using AlvMod.Core.Modulacao;
using AlvMod.Core.Saidas;
using AlvMod.Plugin.Desenho;
using AlvMod.Plugin.Servicos;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AlvMod.Plugin
{
    /// <summary>1ª e 2ª fiada.</summary>
    public sealed partial class Comandos
    {
        [CommandMethod("ALV1FIADA")]
        public void PrimeiraFiada()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var ctx = PrepararObra(doc);
            if (ctx == null) return;
            double fator = ctx.Config.FatorCm;
            var modulador = new Modulador(ctx.Familia);

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var trechos = new List<Trecho>();
                while (true)
                {
                    var t = Geometria.SelecionarTrecho(ed, tr,
                        trechos.Count == 0 ? "\nSelecione o eixo da parede" : "\nSelecione o próximo segmento da mesma parede");
                    if (t == null) break;
                    trechos.Add(t.Normalizado());
                    var outro = Perguntas.SimNao(ed, "\nHá outro segmento desta parede (cortada por porta)?", false);
                    if (outro != true) break;
                }
                if (trechos.Count == 0) return;

                // Segmentos em ordem ao longo da parede; as pontas entre eles são vãos de abertura.
                var refT = trechos[0];
                trechos = trechos.OrderBy(t => (t.Inicio - refT.Inicio).DotProduct(refT.Direcao)).ToList();

                var cotar = Perguntas.SimNao(ed, "\nCotar a parede (trechos e vãos)?", true);
                if (cotar == null) return;

                var desenhista = new Desenhista(doc.Database, tr, fator);
                int fechados = 0;
                for (int i = 0; i < trechos.Count; i++)
                {
                    var aberturas = Bordas.Nenhuma;
                    if (i > 0) aberturas |= Bordas.Inicio;
                    if (i < trechos.Count - 1) aberturas |= Bordas.Fim;

                    var r = modulador.PrimeiraFiada(trechos[i].Comprimento * fator, aberturas);
                    foreach (var p in r.Pecas) desenhista.Desenhar(trechos[i], p);
                    if (r.Sucesso) fechados++;
                    Relatar(doc, "ALV1FIADA", $"Trecho {i + 1}", trechos[i], r);
                }
                if (cotar.Value) desenhista.Cotar(trechos, ctx.Config.Escala);

                tr.Commit();
                Resumo(ed, fechados, trechos.Count, desenhista);
            }
        }

        [CommandMethod("ALV2FIADA")]
        public void SegundaFiada()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var ctx = PrepararObra(doc);
            if (ctx == null) return;
            double fator = ctx.Config.FatorCm;
            var modulador = new Modulador(ctx.Familia);

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var trecho = Geometria.SelecionarTrecho(ed, tr, "\nSelecione o eixo do trecho na planta da 1ª fiada")?.Normalizado();
                if (trecho == null) return;

                var abertura = Perguntas.Opcao(ed,
                    "\nPontas do trecho que encostam em porta/janela (Inicio = ponta esquerda/inferior) [Nenhuma/Inicio/Fim/Ambas]",
                    new[] { "Nenhuma", "Inicio", "Fim", "Ambas" }, "Nenhuma");
                if (abertura == null) return;
                var aberturas = (Bordas)Enum.Parse(typeof(Bordas), abertura);

                var filtro = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT,LWPOLYLINE") });
                var sel = ed.GetSelection(
                    new PromptSelectionOptions { MessageForAdding = "\nSelecione as peças da 1ª fiada deste trecho (já editadas): " },
                    filtro);
                if (sel.Status != PromptStatus.OK) return;

                double comprimento = trecho.Comprimento * fator;
                var juntas = JuntasDaPrimeiraFiada(ed, tr, trecho, sel.Value.GetObjectIds(), fator, comprimento);
                if (juntas == null) return;

                var r = modulador.SegundaFiada(comprimento, juntas, aberturas);
                if (!r.Sucesso && !r.Parcial)
                {
                    Relatar(doc, "ALV2FIADA", "Trecho", trecho, r);
                    return;
                }

                var deslocamento = PerguntarDeslocamento(ed, ctx.Config);
                if (deslocamento == null) return;
                var cotar = Perguntas.SimNao(ed, "\nCotar o trecho?", true);
                if (cotar == null) return;

                var desenhista = new Desenhista(doc.Database, tr, fator, deslocamento.Value);
                foreach (var p in r.Pecas) desenhista.Desenhar(trecho, p);
                if (cotar.Value) desenhista.Cotar(new[] { trecho }, ctx.Config.Escala);
                ObraNoDesenho.Gravar(doc.Database, tr, ctx.Config);
                tr.Commit();

                Relatar(doc, "ALV2FIADA", "Trecho", trecho, r);
                ed.WriteMessage($"\n{juntas.Count} juntas lidas da 1ª fiada.");
                Resumo(ed, r.Sucesso ? 1 : 0, 1, desenhista);
            }
        }

        /// <summary>Centro das juntas da 1ª fiada, projetadas no eixo (cm a partir do início do trecho).</summary>
        private static List<double> JuntasDaPrimeiraFiada(Editor ed, Transaction tr, Trecho trecho,
            IEnumerable<ObjectId> ids, double fator, double comprimento)
        {
            var intervalos = new List<(double ini, double fim)>();
            foreach (var id in ids)
            {
                var cantos = Geometria.Cantos((Entity)tr.GetObject(id, OpenMode.ForRead), tr);
                if (cantos.Count == 0) continue;

                var ts = new List<double>();
                bool foraDaParede = false;
                foreach (var c in cantos)
                {
                    trecho.Projetar(c, out double t, out double d);
                    ts.Add(t * fator);
                    if (Math.Abs(d * fator) > 20) foraDaParede = true; // peça de outra parede
                }
                if (!foraDaParede) intervalos.Add((Math.Max(0, ts.Min()), Math.Min(comprimento, ts.Max())));
            }

            intervalos = intervalos.Where(iv => iv.fim - iv.ini > 1).OrderBy(iv => iv.ini).ToList();
            if (intervalos.Count == 0)
            {
                ed.WriteMessage("\nNenhuma peça da 1ª fiada encontrada sobre este eixo.");
                return null;
            }
            if (intervalos[0].ini > 1 || intervalos[intervalos.Count - 1].fim < comprimento - 1)
                ed.WriteMessage("\n[AVISO] A 1ª fiada selecionada não cobre o trecho inteiro.");

            var juntas = new List<double>();
            for (int i = 0; i + 1 < intervalos.Count; i++)
                juntas.Add((intervalos[i].fim + intervalos[i + 1].ini) / 2);
            return juntas;
        }

        /// <summary>
        /// 1ª e 2ª fiada ficam em plantas separadas no mesmo arquivo (questionário, item 46): o usuário indica
        /// um ponto na planta da 1ª fiada e o correspondente na da 2ª. O deslocamento fica gravado na obra.
        /// </summary>
        private static Vector3d? PerguntarDeslocamento(Editor ed, Core.Catalogo.ConfigObra cfg)
        {
            bool temAnterior = cfg.Deslocamento2F != null && cfg.Deslocamento2F.Length == 3;
            var o = new PromptPointOptions(temAnterior
                ? "\nPonto de referência na planta da 1ª fiada [Anterior/Mesmo lugar] <Anterior>: "
                : "\nPonto de referência na planta da 1ª fiada [Mesmo lugar] <Mesmo lugar>: ");
            o.AppendKeywordsToMessage = false;
            if (temAnterior) o.Keywords.Add("Anterior");
            o.Keywords.Add("Mesmo", "Mesmo", "Mesmo lugar");
            o.AllowNone = true;

            var r = ed.GetPoint(o);
            if (r.Status == PromptStatus.None)
                return temAnterior ? Anterior(cfg) : Vector3d.ZAxis * 0;
            if (r.Status == PromptStatus.Keyword)
                return r.StringResult == "Anterior" ? Anterior(cfg) : Vector3d.ZAxis * 0;
            if (r.Status != PromptStatus.OK) return null;

            var p2 = ed.GetPoint(new PromptPointOptions("\nPonto correspondente na planta da 2ª fiada: ")
            {
                BasePoint = r.Value,
                UseBasePoint = true
            });
            if (p2.Status != PromptStatus.OK) return null;

            var ucs = ed.CurrentUserCoordinateSystem;
            var v = p2.Value.TransformBy(ucs) - r.Value.TransformBy(ucs);
            cfg.Deslocamento2F = new[] { v.X, v.Y, v.Z };
            return v;
        }

        private static Vector3d Anterior(Core.Catalogo.ConfigObra cfg) =>
            new Vector3d(cfg.Deslocamento2F[0], cfg.Deslocamento2F[1], cfg.Deslocamento2F[2]);

        private static void Relatar(Document doc, string comando, string nome, Trecho trecho, ResultadoModulacao r)
        {
            var ed = doc.Editor;
            if (r.Sucesso)
            {
                ed.WriteMessage($"\n{nome}: {r.Mensagem}");
                return;
            }
            ed.WriteMessage($"\n[INCONSISTÊNCIA] {nome}: {r.Mensagem}");
            Registro.Adicionar(doc, Gravidade.Inconsistencia, comando,
                $"{nome}, eixo {trecho.Descrever()}, {Perguntas.Num(r.ComprimentoMedido)} cm", r.Mensagem);
        }

        private static void Resumo(Editor ed, int ok, int total, Desenhista d)
        {
            ed.WriteMessage($"\n{ok} de {total} trecho(s) fecharam, {d.PecasInseridas} peça(s) inserida(s).");
            if (d.BlocosProvisorios.Count > 0)
                ed.WriteMessage("\n[AVISO] Blocos criados como retângulo provisório (não existiam no desenho): " +
                                string.Join(", ", d.BlocosProvisorios));
            if (ok < total)
                ed.WriteMessage("\nUse ALVRELATORIO para gerar o relatório de inconsistências.");
        }
    }
}

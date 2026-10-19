using System;
using System.Collections.Generic;
using System.Linq;
using AlvMod.Core.Catalogo;
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
                Vector3d? espessura = null; // reaproveitada nos segmentos seguintes da mesma parede
                while (true)
                {
                    var t = Geometria.SelecionarTrecho(ed, tr,
                        trechos.Count == 0 ? "\nParede" : "\nPróximo segmento da mesma parede", ref espessura);
                    if (t == null) break;
                    trechos.Add(t.Normalizado());
                    var outro = Perguntas.SimNao(ed, "\nHá outro segmento desta parede (cortada por porta)?", false);
                    if (outro != true) break;
                }
                if (trechos.Count == 0) return;

                // Segmentos em ordem ao longo da parede; entre eles ficam os vãos das aberturas.
                var refT = trechos[0];
                trechos = trechos.OrderBy(t => (t.Inicio - refT.Inicio).DotProduct(refT.Direcao)).ToList();

                // Abertura na alvenaria = tamanho da porta/janela + folga de marco (porta +11, janela +6), centrada
                // no vão desenhado. Quem desenhou o vão já incluiu o marco, então a abertura pode ser menor ou maior.
                var recuoInicio = new double[trechos.Count];
                var recuoFim = new double[trechos.Count];
                var tipos = new TipoAbertura[trechos.Count];
                for (int i = 0; i + 1 < trechos.Count; i++)
                {
                    var tipo = PerguntarAbertura(ed, $"\nO vão {i + 1} (entre os segmentos {i + 1} e {i + 2}) é", false);
                    if (tipo == null) return;
                    tipos[i] = tipo.Value;

                    double vaoCm = trechos[i].Fim.DistanceTo(trechos[i + 1].Inicio) * fator;
                    double folga = tipo == TipoAbertura.Porta ? ctx.Config.FolgaPorta
                                 : tipo == TipoAbertura.Janela ? ctx.Config.FolgaJanela : 0;
                    double tamanho = vaoCm;
                    if (tipo != TipoAbertura.Passagem)
                    {
                        var oque = tipo == TipoAbertura.Porta ? "da porta (folha)" : "da janela (vidro)";
                        var lido = Perguntas.Numero(ed, $"\nTamanho {oque} em cm; Enter = o vão desenhado ({Perguntas.Num(vaoCm)})", vaoCm);
                        if (lido == null) return;
                        tamanho = lido.Value;
                    }
                    double recuo = (tamanho + folga - vaoCm) / 2; // positivo = a alvenaria recua; negativo = avança
                    recuoFim[i] = recuo;
                    recuoInicio[i + 1] = recuo;
                    ed.WriteMessage($"\nAbertura na alvenaria: {Perguntas.Num(tamanho + folga)} cm (tamanho {Perguntas.Num(tamanho)} + {Perguntas.Num(folga)}).");
                }
                trechos = trechos.Select((t, i) => t.Encolher(recuoInicio[i], recuoFim[i], fator)).ToList();
                // A cota mostra todas as aberturas (janela = vidro + 6), mesmo as que não cortam a fiada.
                var pontosCota = trechos.SelectMany(t => new[] { t.Inicio, t.Fim }).ToList();

                // Janela não corta a 1ª e a 2ª fiada (peitoril alto): a alvenaria continua sob ela.
                var modulados = new List<Trecho> { trechos[0] };
                var vaos = new List<TipoAbertura>();
                for (int i = 1; i < trechos.Count; i++)
                {
                    if (tipos[i - 1] == TipoAbertura.Janela)
                        modulados[modulados.Count - 1] = new Trecho(modulados[modulados.Count - 1].Inicio, trechos[i].Fim);
                    else
                    {
                        modulados.Add(trechos[i]);
                        vaos.Add(tipos[i - 1]);
                    }
                }

                // Pontas da parede: em canto/T a parede continua (sem 19 e sem bolacha na ponta).
                var encostam = Perguntas.Opcao(ed, "\nAs pontas da parede encostam em outra parede (canto, T)? [Ambas/Inicio/Fim/Nenhuma]",
                    new[] { "Ambas", "Inicio", "Fim", "Nenhuma" }, "Ambas");
                if (encostam == null) return;
                var pontaInicial = encostam == "Ambas" || encostam == "Inicio" ? TipoAbertura.Parede : TipoAbertura.Livre;
                var pontaFinal = encostam == "Ambas" || encostam == "Fim" ? TipoAbertura.Parede : TipoAbertura.Livre;

                var cotar = Perguntas.SimNao(ed, "\nCotar a parede (trechos e vãos)?", true);
                if (cotar == null) return;

                var desenhista = new Desenhista(doc.Database, tr, fator);
                int fechados = 0;
                for (int i = 0; i < modulados.Count; i++)
                {
                    var pontas = PontasDoVao(i > 0 ? vaos[i - 1] : pontaInicial,
                                             i < modulados.Count - 1 ? vaos[i] : pontaFinal);

                    double comprimento = modulados[i].Comprimento * fator;
                    var r = modulador.PrimeiraFiada(comprimento, pontas);
                    foreach (var p in r.Pecas) desenhista.Desenhar(modulados[i], p);
                    if (r.Sucesso) fechados++;
                    string nome = $"Trecho {i + 1}";
                    Registro.Adicionar(doc, MemoriaCalculo.Linha("ALV1FIADA", nome, 1, comprimento, pontas, r));
                    Relatar(doc, "ALV1FIADA", nome, modulados[i], r);
                }
                if (cotar.Value) desenhista.CotarPontos(trechos[0], pontosCota, ctx.Config.Escala, LarguraParede(ctx));

                tr.Commit();
                Resumo(ed, fechados, modulados.Count, desenhista);
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
                Vector3d? espessura = null;
                var trecho = Geometria.SelecionarTrecho(ed, tr, "\nTrecho na planta da 1ª fiada", ref espessura)?.Normalizado();
                if (trecho == null) return;

                // O que há em cada ponta define as peças (19 na porta, nada de 19 nem bolacha encostado em parede).
                var tipoInicio = PerguntarAbertura(ed, "\nA ponta inicial (esquerda/inferior) encosta em", true);
                if (tipoInicio == null) return;
                var tipoFim = PerguntarAbertura(ed, "\nA ponta final (direita/superior) encosta em", true);
                if (tipoFim == null) return;
                var pontas = PontasDoVao(tipoInicio, tipoFim);

                var filtro = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT,LWPOLYLINE") });
                var sel = ed.GetSelection(
                    new PromptSelectionOptions { MessageForAdding = "\nSelecione as peças da 1ª fiada deste trecho (já editadas): " },
                    filtro);
                if (sel.Status != PromptStatus.OK) return;

                var lida = JuntasDaPrimeiraFiada(ed, tr, trecho, sel.Value.GetObjectIds(), fator, trecho.Comprimento * fator);
                if (lida == null) return;
                var juntas = lida.Value.juntas;

                // A 2ª fiada ocupa exatamente o trecho da 1ª (mesmas aberturas, já com o tamanho da porta + folga).
                trecho = new Trecho(trecho.PontoEm(lida.Value.ini / fator), trecho.PontoEm(lida.Value.fim / fator));
                double comprimento = trecho.Comprimento * fator;
                juntas = juntas.Select(j => j - lida.Value.ini).ToList();

                var r = modulador.SegundaFiada(comprimento, juntas, pontas);
                Registro.Adicionar(doc, MemoriaCalculo.Linha("ALV2FIADA", "Trecho", 2, comprimento, pontas, r));
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
                if (cotar.Value) desenhista.Cotar(new[] { trecho }, ctx.Config.Escala, LarguraParede(ctx));
                ObraNoDesenho.Gravar(doc.Database, tr, ctx.Config);
                tr.Commit();

                Relatar(doc, "ALV2FIADA", "Trecho", trecho, r);
                ed.WriteMessage($"\n{juntas.Count} juntas lidas da 1ª fiada.");
                Resumo(ed, r.Sucesso ? 1 : 0, 1, desenhista);
            }
        }

        /// <summary>
        /// O que há num vão (entre dois segmentos: porta, janela, passagem) ou numa ponta de trecho
        /// (<paramref name="ponta"/>: outra parede, ponta livre, porta ou passagem).
        /// </summary>
        private static TipoAbertura? PerguntarAbertura(Editor ed, string mensagem, bool ponta)
        {
            var opcoes = ponta
                ? new[] { "Parede", "Livre", "Porta", "Passagem" }
                : new[] { "Porta", "Janela", "Passagem" };
            var r = Perguntas.Opcao(ed, $"{mensagem} [{string.Join("/", opcoes)}]", opcoes, opcoes[0]);
            return r == null ? (TipoAbertura?)null : (TipoAbertura)Enum.Parse(typeof(TipoAbertura), r);
        }

        /// <summary>
        /// Pontas de um trecho a partir do que existe em cada uma, com as mesmas regras do ALVPLANTA:
        /// porta/passagem exigem o 19; outra parede (canto, T) proíbe o 19; nas duas, nada de bolacha.
        /// </summary>
        private static Pontas PontasDoVao(TipoAbertura? inicio, TipoAbertura? fim)
        {
            var p = new Pontas();
            void Marca(TipoAbertura? t, Bordas lado)
            {
                if (t == null || t == TipoAbertura.Livre) return;
                p.SemCompensador |= lado;
                if (t == TipoAbertura.Porta || t == TipoAbertura.Passagem) p.Porta |= lado;
                if (t == TipoAbertura.Parede) p.Encontro |= lado;
            }
            Marca(inicio, Bordas.Inicio);
            Marca(fim, Bordas.Fim);
            return p;
        }

        private static int LarguraParede(ContextoObra ctx) =>
            ctx.Familia.PecasDeFiada().Select(p => p.Largura).DefaultIfEmpty(14).Max();

        /// <summary>Centro das juntas da 1ª fiada, projetadas no eixo (cm a partir do início do trecho).</summary>
        private static (List<double> juntas, double ini, double fim)? JuntasDaPrimeiraFiada(Editor ed, Transaction tr, Trecho trecho,
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
            return (juntas, intervalos[0].ini, intervalos[intervalos.Count - 1].fim);
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
            foreach (var aviso in r.Avisos)
                Registro.Adicionar(doc, Gravidade.Aviso, comando,
                    $"{nome}, eixo {trecho.Descrever()}, {Perguntas.Num(r.ComprimentoMedido)} cm", aviso);
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
            foreach (var par in d.BlocosAjustados)
                ed.WriteMessage($"\n[AVISO] Bloco {par.Key} {par.Value}: escala ajustada na inserção para as peças ficarem a 1 cm.");
            if (ok < total)
                ed.WriteMessage("\nUse ALVRELATORIO para gerar o relatório de inconsistências.");
        }
    }
}

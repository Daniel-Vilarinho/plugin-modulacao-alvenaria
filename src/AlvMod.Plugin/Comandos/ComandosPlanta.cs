using System;
using System.Collections.Generic;
using System.Linq;
using AlvMod.Core.Catalogo;
using AlvMod.Core.Modulacao;
using AlvMod.Core.Planta;
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
    /// <summary>
    /// Pavimento inteiro: lê a arquitetura (paredes pelas faces, portas, janelas, encontros), numera as
    /// paredes e modula a 1ª fiada (ALVPLANTA) ou a 2ª a partir da 1ª desenhada (ALVPLANTA2).
    /// </summary>
    public sealed partial class Comandos
    {
        private const string LayerNumeracao = "NUMERACAO PAREDES";
        private const string LayerEixos = "ALV-EIXOS";

        [CommandMethod("ALVPLANTA")]
        public void PlantaPrimeiraFiada()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var ctx = PrepararObra(doc);
            if (ctx == null) return;
            if (!PerguntarLayers(ed, ctx.Config)) return;
            GravarConfig(doc, ctx.Config);

            var sel = SelecionarPavimento(ed, "\nSelecione a planta de arquitetura do pavimento (janela): ");
            if (sel == null) return;
            sel = OferecerApagarAnterior(doc, sel, ctx.Catalogo);
            if (sel == null) return;
            Registro.Reiniciar(doc, "ALVPLANTA");

            double fator = ctx.Config.FatorCm;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var leitor = new LeitorPlanta(fator);
                leitor.Ler(tr, sel);
                var planta = Detectar(ed, leitor, ctx.Config);
                if (planta == null) return;

                var desenhista = new Desenhista(doc.Database, tr, fator);
                DesenharEixosENumeracao(doc.Database, tr, planta, fator, ctx.Config.Escala, new Vector3d());

                var confirma = Perguntas.SimNao(ed,
                    $"\n{Resumo(planta)}\nEixos e numeração desenhados nos layers \"{LayerEixos}\" e \"{LayerNumeracao}\". Modular a 1ª fiada?", true);
                if (confirma != true)
                {
                    tr.Commit(); // mantém eixos e numeração para conferência
                    ed.WriteMessage("\nSó a detecção foi desenhada. Ajuste os layers/desenho e rode ALVPLANTA de novo.");
                    return;
                }
                var cotar = Perguntas.SimNao(ed, "\nCotar as paredes?", true);
                if (cotar == null) return;

                var modulador = new Modulador(ctx.Familia);
                var opF = CriarOpcoesFiadas(ctx);
                var avisos = new List<string>();
                var plano = PlanejadorFiadas.Planejar(planta, 1, opF, avisos);
                var trechos = plano.Trechos;
                DesenharFixas(desenhista, plano.Fixas, fator);
                int fechados = ModularEDesenhar(doc, "ALVPLANTA", trechos, fator, desenhista,
                    t => modulador.PrimeiraFiada(t.Comprimento, t.Pontas));
                if (cotar.Value) Cotar(desenhista, planta, trechos, plano.Fixas, fator, ctx, opF);

                tr.Commit();
                foreach (var a in avisos) Registro.Adicionar(doc, Gravidade.Aviso, "ALVPLANTA", null, a);
                Finalizar(ed, fechados, trechos.Count, desenhista, avisos.Count);
            }
        }

        [CommandMethod("ALVPLANTA2")]
        public void PlantaSegundaFiada()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var ctx = PrepararObra(doc);
            if (ctx == null) return;
            if (ctx.Config.LayersParede.Count == 0)
            {
                ed.WriteMessage("\nRode o ALVPLANTA primeiro (ele grava os layers da arquitetura desta obra).");
                return;
            }

            var sel = SelecionarPavimento(ed,
                "\nSelecione a planta da 1ª fiada (arquitetura + blocos já conferidos/editados): ");
            if (sel == null) return;
            Registro.Reiniciar(doc, "ALVPLANTA2");

            double fator = ctx.Config.FatorCm;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var leitor = new LeitorPlanta(fator);
                leitor.Ler(tr, sel);
                var planta = Detectar(ed, leitor, ctx.Config);
                if (planta == null) return;

                // Peças da 1ª fiada como estão desenhadas, por parede.
                var pecas1 = PecasDesenhadas(tr, leitor.Blocos, ctx.Catalogo, fator);
                if (pecas1.Count == 0)
                {
                    ed.WriteMessage("\nNenhum bloco do catálogo na seleção: rode o ALVPLANTA (1ª fiada) antes.");
                    return;
                }

                var deslocamento = PerguntarDeslocamento(ed, ctx.Config);
                if (deslocamento == null) return;
                var cotar = Perguntas.SimNao(ed, "\nCotar as paredes?", true);
                if (cotar == null) return;

                var modulador = new Modulador(ctx.Familia);
                var opF = CriarOpcoesFiadas(ctx);
                var avisos = new List<string>();
                var plano = PlanejadorFiadas.Planejar(planta, 2, opF, avisos);
                var trechos = plano.Trechos;

                var desenhista = new Desenhista(doc.Database, tr, fator, deslocamento.Value);
                DesenharEixosENumeracao(doc.Database, tr, planta, fator, ctx.Config.Escala, deslocamento.Value);
                DesenharFixas(desenhista, plano.Fixas, fator);
                int fechados = ModularEDesenhar(doc, "ALVPLANTA2", trechos, fator, desenhista, t =>
                {
                    var arestas = PlanejadorFiadas.CentrosDeJunta(ArestasDaParede(pecas1, t.Parede), opF.Junta)
                        .Select(a => a - t.Inicio).ToList();
                    return modulador.SegundaFiada(t.Comprimento, arestas, t.Pontas);
                });
                if (cotar.Value) Cotar(desenhista, planta, trechos, plano.Fixas, fator, ctx, opF);
                ObraNoDesenho.Gravar(doc.Database, tr, ctx.Config);

                tr.Commit();
                foreach (var a in avisos) Registro.Adicionar(doc, Gravidade.Aviso, "ALVPLANTA2", null, a);
                ed.WriteMessage($"\n{pecas1.Count} peças da 1ª fiada lidas do desenho.");
                Finalizar(ed, fechados, trechos.Count, desenhista, avisos.Count);
            }
        }

        // ------------------------------------------------------------------ etapas

        /// <summary>
        /// Detecção assistida: o usuário clica uma linha de cada tipo (parede, porta, janela) e o plugin
        /// passa a ler os layers delas. Enter mantém os layers já gravados na obra.
        /// </summary>
        private static bool PerguntarLayers(Editor ed, ConfigObra cfg)
        {
            var parede = PerguntarLayersDe(ed, "uma linha de PAREDE (face)", cfg.LayersParede, obrigatorio: true);
            if (parede == null) return false;
            var porta = PerguntarLayersDe(ed, "uma linha de PORTA (marco, folha ou arco)", cfg.LayersPorta, obrigatorio: false);
            if (porta == null) return false;
            var janela = PerguntarLayersDe(ed, "uma linha de JANELA (peitoril ou vidro)", cfg.LayersJanela, obrigatorio: false);
            if (janela == null) return false;
            cfg.LayersParede = parede;
            cfg.LayersPorta = porta;
            cfg.LayersJanela = janela;
            return true;
        }

        private static List<string> PerguntarLayersDe(Editor ed, string oQue, List<string> atuais, bool obrigatorio)
        {
            var layers = new List<string>();
            while (true)
            {
                string padrao = layers.Count > 0 ? "terminar" : atuais.Count > 0 ? "manter " + string.Join(", ", atuais) : "nenhum";
                var o = new PromptNestedEntityOptions($"\nClique {oQue} <Enter = {padrao}>: ") { AllowNone = true };
                var r = ed.GetNestedEntity(o);
                if (r.Status == PromptStatus.None)
                {
                    if (layers.Count == 0) layers = atuais.ToList();
                    if (obrigatorio && layers.Count == 0) { ed.WriteMessage("\nÉ preciso indicar o layer das paredes."); continue; }
                    return layers;
                }
                if (r.Status != PromptStatus.OK) return null;

                using (var tr = r.ObjectId.Database.TransactionManager.StartOpenCloseTransaction())
                {
                    var ent = (Entity)tr.GetObject(r.ObjectId, OpenMode.ForRead);
                    string layer = ent.Layer;
                    // Entidade no layer 0 dentro de bloco: vale o layer do bloco que a contém.
                    if (layer == "0" && r.GetContainers().Length > 0)
                        layer = ((Entity)tr.GetObject(r.GetContainers()[0], OpenMode.ForRead)).Layer;
                    if (!layers.Contains(layer, StringComparer.OrdinalIgnoreCase)) layers.Add(layer);
                    ed.WriteMessage($"\nLayer \"{layer}\" adicionado. Clique outra linha do mesmo tipo em outro layer, ou Enter.");
                }
            }
        }

        /// <summary>
        /// Rodar o ALVPLANTA de novo na mesma planta duplicaria blocos, eixos, numeração e cotas: se a seleção
        /// tem objetos de uma modulação anterior, pergunta se apaga. Devolve a seleção sem eles (ou como
        /// estava, se o usuário preferir manter), ou nulo se cancelou.
        /// </summary>
        private static ObjectId[] OferecerApagarAnterior(Document doc, ObjectId[] sel, CatalogoBlocos catalogo)
        {
            var layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { LayerEixos, LayerNumeracao, Desenhista.LayerCotas };
            var anteriores = new List<ObjectId>();
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (var id in sel)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    bool nosso = layers.Contains(ent.Layer);
                    if (!nosso && ent is BlockReference br)
                    {
                        var defId = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
                        nosso = catalogo.BuscarPorBloco(((BlockTableRecord)tr.GetObject(defId, OpenMode.ForRead)).Name) != null;
                    }
                    if (nosso) anteriores.Add(id);
                }
            }
            if (anteriores.Count == 0) return sel;

            var apagar = Perguntas.SimNao(doc.Editor,
                $"\nA seleção já tem {anteriores.Count} objeto(s) de uma modulação anterior (blocos do catálogo, eixos, " +
                "numeração, cotas). Apagar antes de modular de novo?", true);
            if (apagar == null) return null;
            if (!apagar.Value) return sel;

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (var id in anteriores) tr.GetObject(id, OpenMode.ForWrite).Erase();
                tr.Commit();
            }
            doc.Editor.WriteMessage($"\n{anteriores.Count} objeto(s) da modulação anterior apagados.");
            var apagados = new HashSet<ObjectId>(anteriores);
            return sel.Where(id => !apagados.Contains(id)).ToArray();
        }

        private static ObjectId[] SelecionarPavimento(Editor ed, string mensagem)
        {
            var r = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = mensagem });
            return r.Status == PromptStatus.OK ? r.Value.GetObjectIds() : null;
        }

        private static PlantaDetectada Detectar(Editor ed, LeitorPlanta leitor, ConfigObra cfg)
        {
            var op = new OpcoesDeteccao();
            foreach (var l in cfg.LayersParede) op.LayersParede.Add(l);
            foreach (var l in cfg.LayersPorta) op.LayersPorta.Add(l);
            foreach (var l in cfg.LayersJanela) op.LayersJanela.Add(l);

            var planta = DetectorParedes.Detectar(leitor.Segmentos, op, leitor.Rotulos);
            foreach (var a in planta.Avisos) ed.WriteMessage("\n[AVISO] " + a);
            if (planta.Paredes.Count == 0)
            {
                ed.WriteMessage($"\nNenhuma parede encontrada nos layers {string.Join(", ", cfg.LayersParede)} " +
                                "(paredes pelas duas faces, ortogonais, 5 a 35 cm de espessura).");
                var sugestao = DetectorParedes.SugerirFatorCm(leitor.Segmentos, cfg.FatorCm, op);
                ed.WriteMessage(sugestao.HasValue
                    ? $"\nCom a unidade {Perguntas.Num(sugestao.Value)} as paredes aparecem: rode de novo e responda " +
                      $"{Perguntas.Num(sugestao.Value)} em \"Quantos cm vale 1 unidade do desenho\" (agora está {Perguntas.Num(cfg.FatorCm)})."
                    : "\nConfira a unidade do desenho (1 = cm, 100 = metro) e se a linha clicada é uma face de parede.");
                return null;
            }
            Numeracao.Numerar(planta);
            return planta;
        }

        private static string Resumo(PlantaDetectada p)
        {
            var vaos = p.Paredes.SelectMany(w => w.Vaos).ToList();
            return $"Detectado: {p.Paredes.Count} paredes; encontros L {p.Encontros.Count(e => e.Tipo == TipoEncontro.L)}, " +
                   $"T {p.Encontros.Count(e => e.Tipo == TipoEncontro.T)}, X {p.Encontros.Count(e => e.Tipo == TipoEncontro.X)}; " +
                   $"{vaos.Count(v => v.Tipo == TipoVao.Porta)} portas, {vaos.Count(v => v.Tipo == TipoVao.Janela)} janelas, " +
                   $"{vaos.Count(v => v.Tipo == TipoVao.Passagem)} passagens.";
        }

        private static OpcoesFiadas CriarOpcoesFiadas(ContextoObra ctx) => new OpcoesFiadas
        {
            Largura = LarguraParede(ctx),
            Junta = ctx.Familia.Junta,
            FolgaPorta = ctx.Config.FolgaPorta,
            FolgaJanela = ctx.Config.FolgaJanela,
            // 54 fixo e centrado nos encontros em T e em cruz (se a obra usa o 54).
            BlocoEncontro = ctx.Familia.Pecas.FirstOrDefault(p => p.Funcao == FuncaoPeca.Encontro && p.Habilitada)
        };

        /// <summary>Desenha as peças fixas (o 54 centrado nos encontros em T e em cruz).</summary>
        private static void DesenharFixas(Desenhista desenhista, List<PecaFixa> fixas, double fator)
        {
            foreach (var f in fixas)
            {
                var (x1, y1) = f.Parede.Ponto(f.Inicio);
                var (x2, y2) = f.Parede.Ponto(f.Fim);
                var geo = new Trecho(new Point3d(x1 / fator, y1 / fator, 0), new Point3d(x2 / fator, y2 / fator, 0));
                desenhista.Desenhar(geo, new PecaPosicionada { Peca = f.Peca, Inicio = 0 });
            }
        }

        private static int ModularEDesenhar(Document doc, string comando, List<TrechoFiada> trechos, double fator,
            Desenhista desenhista, Func<TrechoFiada, ResultadoModulacao> modular)
        {
            int fechados = 0;
            foreach (var t in trechos)
            {
                var r = modular(t);
                Registro.Adicionar(doc, MemoriaCalculo.Linha(comando, t.Nome, t.Fiada, t.Comprimento, t.Pontas, r));
                var geo = Geometrico(t, fator);
                foreach (var p in r.Pecas) desenhista.Desenhar(geo, p);
                if (r.Sucesso) { fechados++; if (r.Avisos.Count == 0) continue; }
                if (r.Pecas.Count == 0 && t.Comprimento < 40)
                    r.Mensagem = $"boneca de {t.Comprimento:0.#} cm entre porta e encontro: nenhum bloco cabe pelas regras " +
                                 "(19 na porta, 34/39 no encontro); resolver à mão ou ajustar a posição da porta. " + r.Mensagem;
                Relatar(doc, comando, t.Nome, geo, r);
            }
            return fechados;
        }

        /// <summary>Trecho do núcleo (cm, ao longo do eixo) → trecho no desenho (unidades do desenho).</summary>
        private static Trecho Geometrico(TrechoFiada t, double fator)
        {
            var (x1, y1) = t.Parede.Ponto(t.Inicio);
            var (x2, y2) = t.Parede.Ponto(t.Fim);
            return new Trecho(new Point3d(x1 / fator, y1 / fator, 0), new Point3d(x2 / fator, y2 / fator, 0));
        }

        /// <summary>
        /// Cota corrida de cada parede, pela face: parede, porta (tamanho + 11), parede, janela (tamanho + 6)...
        /// As janelas não cortam a fiada, mas entram na cota.
        /// </summary>
        private static void Cotar(Desenhista desenhista, PlantaDetectada planta, List<TrechoFiada> trechos, List<PecaFixa> fixas,
            double fator, ContextoObra ctx, OpcoesFiadas opF)
        {
            foreach (var w in trechos.Select(t => t.Parede).Concat(fixas.Select(f => f.Parede)).Distinct())
            {
                var (x1, y1) = w.Ponto(w.Inicio);
                var (x2, y2) = w.Ponto(w.Fim);
                var referencia = new Trecho(new Point3d(x1 / fator, y1 / fator, 0), new Point3d(x2 / fator, y2 / fator, 0));
                var pontos = PlanejadorFiadas.PontosDeCota(w, trechos, opF, fixas).Select(t =>
                {
                    var (x, y) = w.Ponto(t);
                    return new Point3d(x / fator, y / fator, 0);
                }).ToList();
                desenhista.CotarPontos(referencia, pontos, ctx.Config.Escala, LarguraParede(ctx));
            }
        }

        private static void DesenharEixosENumeracao(Database db, Transaction tr, PlantaDetectada planta, double fator,
            double escala, Vector3d deslocamento)
        {
            var espaco = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            GarantirLayer(db, tr, LayerEixos, 9);
            GarantirLayer(db, tr, LayerNumeracao, 1);
            double altura = 2.5 * escala / (10.0 * fator); // 2,5 mm no papel

            foreach (var w in planta.Paredes)
            {
                var (x1, y1) = w.Ponto(w.Inicio);
                var (x2, y2) = w.Ponto(w.Fim);
                var eixo = new Line(new Point3d(x1 / fator, y1 / fator, 0) + deslocamento,
                    new Point3d(x2 / fator, y2 / fator, 0) + deslocamento) { Layer = LayerEixos };
                espaco.AppendEntity(eixo);
                tr.AddNewlyCreatedDBObject(eixo, true);

                // Nome do lado de cima (horizontais) / da esquerda (verticais), afastado da face.
                var (mx, my) = w.Ponto((w.Inicio + w.Fim) / 2);
                double afast = (w.Espessura / 2 + 3) / fator + altura * 0.3;
                var pos = w.Horizontal
                    ? new Point3d(mx / fator, my / fator + afast, 0)
                    : new Point3d(mx / fator - afast, my / fator, 0);
                var texto = new DBText
                {
                    TextString = w.Nome,
                    Height = altura,
                    Layer = LayerNumeracao,
                    Rotation = w.Horizontal ? 0 : Math.PI / 2,
                    HorizontalMode = TextHorizontalMode.TextCenter,
                    VerticalMode = TextVerticalMode.TextBottom,
                    AlignmentPoint = pos + deslocamento,
                    Position = pos + deslocamento
                };
                espaco.AppendEntity(texto);
                tr.AddNewlyCreatedDBObject(texto, true);
                texto.AdjustAlignment(db);
            }
        }

        private static void GarantirLayer(Database db, Transaction tr, string nome, short cor)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(nome)) return;
            lt.UpgradeOpen();
            var l = new LayerTableRecord
            {
                Name = nome,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, cor)
            };
            lt.Add(l);
            tr.AddNewlyCreatedDBObject(l, true);
        }

        /// <summary>Peça desenhada da 1ª fiada: retângulo em cm e se está alinhada na horizontal.</summary>
        private sealed class PecaDesenhada
        {
            public double X0, Y0, X1, Y1;
            public bool Horizontal;
        }

        private static List<PecaDesenhada> PecasDesenhadas(Transaction tr, List<BlockReference> blocos,
            CatalogoBlocos catalogo, double fator)
        {
            var pecas = new List<PecaDesenhada>();
            foreach (var br in blocos)
            {
                var defId = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
                var nome = ((BlockTableRecord)tr.GetObject(defId, OpenMode.ForRead)).Name;
                if (catalogo.BuscarPorBloco(nome) == null) continue;

                var cantos = Geometria.Cantos(br, tr);
                if (cantos.Count == 0) continue;
                double dx = Math.Cos(br.Rotation), dy = Math.Sin(br.Rotation);
                bool horizontal = Math.Abs(dy) < 0.1, vertical = Math.Abs(dx) < 0.1;
                if (!horizontal && !vertical) continue; // parede inclinada: fora da leitura automática
                pecas.Add(new PecaDesenhada
                {
                    X0 = cantos.Min(c => c.X) * fator,
                    Y0 = cantos.Min(c => c.Y) * fator,
                    X1 = cantos.Max(c => c.X) * fator,
                    Y1 = cantos.Max(c => c.Y) * fator,
                    Horizontal = horizontal
                });
            }
            return pecas;
        }

        /// <summary>Arestas (cm, ao longo do eixo) das peças da 1ª fiada alinhadas com a parede.</summary>
        private static List<double> ArestasDaParede(List<PecaDesenhada> pecas, Parede w)
        {
            var arestas = new List<double>();
            foreach (var p in pecas.Where(p => p.Horizontal == w.Horizontal))
            {
                double centroTrans = w.Horizontal ? (p.Y0 + p.Y1) / 2 : (p.X0 + p.X1) / 2;
                double a = w.Horizontal ? p.X0 : p.Y0, b = w.Horizontal ? p.X1 : p.Y1;
                if (Math.Abs(centroTrans - w.Coord) > 4) continue;           // outra parede paralela
                if (b < w.Inicio - 60 || a > w.Fim + 60) continue;           // fora da extensão da parede
                arestas.Add(a);
                arestas.Add(b);
            }
            return arestas;
        }

        private static void Finalizar(Editor ed, int fechados, int total, Desenhista d, int avisos)
        {
            ed.WriteMessage($"\n{fechados} de {total} trecho(s) fecharam exatamente; {d.PecasInseridas} peça(s) inseridas.");
            if (avisos > 0) ed.WriteMessage($"\n{avisos} aviso(s) registrados.");
            if (d.BlocosProvisorios.Count > 0)
                ed.WriteMessage("\n[AVISO] Blocos criados como retângulo provisório (não existiam no desenho): " +
                                string.Join(", ", d.BlocosProvisorios));
            foreach (var par in d.BlocosAjustados)
                ed.WriteMessage($"\n[AVISO] Bloco {par.Key} {par.Value}: escala ajustada na inserção para as peças ficarem a 1 cm.");
            if (fechados < total)
                ed.WriteMessage("\nTrechos que não fecharam estão no relatório: ALVRELATORIO.");
        }
    }
}

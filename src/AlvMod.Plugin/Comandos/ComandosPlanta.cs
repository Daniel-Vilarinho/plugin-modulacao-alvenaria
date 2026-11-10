using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// Pavimento inteiro, do jeito que o engenheiro modula à mão:
    ///  - ALVPLANTA lê a arquitetura (paredes pelas faces, portas, janelas, encontros), confere a unidade pela
    ///    espessura das paredes, deixa marcar o que não é alvenaria (drywall, shaft), põe os eixos na grade de
    ///    5 cm, decide quem passa em cada encontro e onde cada porta fica (até 12 cm) para tudo fechar com o
    ///    mínimo de peças especiais, e desenha a 1ª fiada com graute, nomes por tipo, aberturas, cotas e título;
    ///  - ALVPLANTA2 faz a 2ª fiada em cima da 1ª como ela está desenhada (respeita as edições).
    /// </summary>
    public sealed partial class Comandos
    {
        private const string LayerEixos = "ALV-EIXOS";

        /// <summary>Layers que versões anteriores do plugin desenhavam (também apagados ao refazer).</summary>
        private static readonly string[] LayersAntigos = { "NUMERACAO PAREDES", "COTAS ALVENARIA" };

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

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var leitura = Ler(doc, tr, sel, ctx);
                if (leitura == null) return;

                var conferencia = DesenharEixos(doc.Database, tr, leitura.Planta, ctx.Config.FatorCm, ctx.Config.Escala);
                ed.WriteMessage($"\n{Resumo(leitura.Planta)}");
                ed.WriteMessage($"\nEixos e números provisórios desenhados no layer \"{LayerEixos}\" para conferência.");

                var confirma = Perguntas.SimNao(ed, "\nModular a 1ª fiada?", true);
                if (confirma != true)
                {
                    ObraNoDesenho.Gravar(doc.Database, tr, ctx.Config);
                    tr.Commit(); // mantém eixos e números para conferência
                    ed.WriteMessage("\nSó a detecção foi desenhada. Ajuste e rode ALVPLANTA de novo.");
                    return;
                }
                if (!PerguntarJanelas(ed, leitura.Planta, ctx.Config)) return;
                var cotar = Perguntas.SimNao(ed, "\nCotar (cotas internas por cômodo)?", true);
                if (cotar == null) return;
                ObraNoDesenho.Gravar(doc.Database, tr, ctx.Config);

                double fator = ctx.Config.FatorCm;
                var plano = Planejar(ctx, leitura.Planta, null);
                Nomear(ctx, leitura.Planta, plano.Solucao, plano.Modulacao);
                foreach (var id in conferencia.Nomes) tr.GetObject(id, OpenMode.ForWrite).Erase();

                var desenhista = new Desenhista(doc.Database, tr, fator);
                var anotador = new Anotador(doc.Database, tr, fator, ctx.Config.Escala);
                var (fechados, total) = DesenharFiada(doc, "ALVPLANTA", desenhista, plano.Solucao, plano.Modulacao.Fiada1, 1, fator);
                Anotar(anotador, leitura.Planta, plano.Solucao, plano.Opcoes, ctx, cotar.Value, "Planta de 1ª fiada - Modulação");

                tr.Commit();
                RegistrarDecisoes(doc, "ALVPLANTA", leitura, plano.Solucao);
                Finalizar(ed, fechados, total, desenhista, anotador, plano.Solucao);
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

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var leitura = Ler(doc, tr, sel, ctx);
                if (leitura == null) return;
                double fator = ctx.Config.FatorCm;
                AplicarQuadroJanelas(leitura.Planta, ctx.Config);

                // Peças da 1ª fiada como estão desenhadas (com as edições do projetista).
                var pecas1 = PecasDesenhadas(tr, leitura.Leitor.Blocos, ctx.Catalogo, fator);
                if (pecas1.Count == 0)
                {
                    ed.WriteMessage("\nNenhum bloco do catálogo na seleção: rode o ALVPLANTA (1ª fiada) antes.");
                    return;
                }

                // Quem passa em cada encontro na 1ª fiada desenhada: na 2ª fiada inverte.
                var fixos = new Dictionary<Encontro, bool>();
                foreach (var e in leitura.Planta.Encontros)
                {
                    var p = pecas1.FirstOrDefault(q => q.X0 - 1 <= e.X && e.X <= q.X1 + 1 && q.Y0 - 1 <= e.Y && e.Y <= q.Y1 + 1);
                    if (p != null) fixos[e] = !p.Horizontal;
                }

                var deslocamento = PerguntarDeslocamento(ed, ctx.Config);
                if (deslocamento == null) return;
                var cotar = Perguntas.SimNao(ed, "\nCotar (cotas internas por cômodo)?", true);
                if (cotar == null) return;

                var plano = Planejar(ctx, leitura.Planta, fixos);
                Nomear(ctx, leitura.Planta, plano.Solucao, plano.Modulacao);

                // 2ª fiada amarrada nas juntas reais da 1ª desenhada.
                var fiada2 = plano.Solucao.Fiada2.Trechos.Select(t =>
                {
                    var arestas = PlanejadorFiadas.CentrosDeJunta(ArestasDaParede(pecas1, t.Parede), plano.Opcoes.Junta)
                        .Select(a => a - t.Inicio).ToList();
                    return (trecho: t, resultado: plano.Modulador.SegundaFiada(t.Comprimento, arestas, t.Pontas));
                }).ToList();

                var desenhista = new Desenhista(doc.Database, tr, fator, deslocamento.Value);
                var anotador = new Anotador(doc.Database, tr, fator, ctx.Config.Escala, deslocamento.Value);
                var (fechados, total) = DesenharFiada(doc, "ALVPLANTA2", desenhista, plano.Solucao, fiada2, 2, fator);
                Anotar(anotador, leitura.Planta, plano.Solucao, plano.Opcoes, ctx, cotar.Value, "Planta de 2ª fiada - Modulação");
                ObraNoDesenho.Gravar(doc.Database, tr, ctx.Config);

                tr.Commit();
                RegistrarDecisoes(doc, "ALVPLANTA2", leitura, plano.Solucao);
                ed.WriteMessage($"\n{pecas1.Count} peças da 1ª fiada lidas do desenho; {fixos.Count} encontros com a 1ª fiada desenhada.");
                Finalizar(ed, fechados, total, desenhista, anotador, plano.Solucao);
            }
        }

        // ------------------------------------------------------------------ leitura

        private sealed class Leitura
        {
            public PlantaDetectada Planta;
            public LeitorPlanta Leitor;
            public double AjusteGrade;
        }

        /// <summary>
        /// Lê a seleção e detecta as paredes. Confere a unidade pela espessura das paredes (bloco de 14: 14 a 20 cm);
        /// se não bater, sugere a unidade certa (ex.: arquitetura em 1:50 lida como metro → 50) e lê de novo.
        /// Põe os eixos na grade de 5 cm e numera provisoriamente.
        /// </summary>
        /// <param name="conferirUnidade">Falso ao ler de novo a mesma seleção (a unidade já foi conferida).</param>
        private static Leitura Ler(Document doc, Transaction tr, ObjectId[] sel, ContextoObra ctx, bool conferirUnidade = true)
        {
            var ed = doc.Editor;
            for (int tentativa = 0; tentativa < 2; tentativa++)
            {
                double fator = ctx.Config.FatorCm;
                var leitor = new LeitorPlanta(fator);
                leitor.Ler(tr, sel);
                var op = OpcoesDeteccao(ctx.Config);
                var planta = DetectorParedes.Detectar(leitor.Segmentos, op, leitor.Rotulos);

                if (DetectorParedes.EspessuraSuspeita(planta) && (conferirUnidade || planta.Paredes.Count == 0))
                {
                    ed.WriteMessage(planta.Paredes.Count == 0
                        ? $"\nNenhuma parede de alvenaria encontrada com a unidade {Perguntas.Num(fator)} (1 unidade = {Perguntas.Num(fator)} cm)."
                        : $"\nCom a unidade {Perguntas.Num(fator)} as paredes medem {Perguntas.Num(planta.EspessuraTipica)} cm; parede de bloco 14 mede 14 a 20 cm.");
                    var sugestao = tentativa == 0 && conferirUnidade ? DetectorParedes.SugerirFatorCm(leitor.Segmentos, fator, op) : null;
                    if (sugestao.HasValue)
                    {
                        var usar = Perguntas.SimNao(ed,
                            $"\nCom a unidade {Perguntas.Num(sugestao.Value)} (1 unidade = {Perguntas.Num(sugestao.Value)} cm) as paredes ficam com espessura de bloco. Usar {Perguntas.Num(sugestao.Value)}?",
                            true);
                        if (usar == null) return null;
                        if (usar == true)
                        {
                            ctx.Config.FatorCm = sugestao.Value;
                            ObraNoDesenho.Gravar(doc.Database, tr, ctx.Config);
                            continue;
                        }
                    }
                    if (planta.Paredes.Count == 0)
                    {
                        ed.WriteMessage("\nConfira a unidade do desenho e se a linha clicada como PAREDE é uma face de parede.");
                        return null;
                    }
                }

                foreach (var a in planta.Avisos) ed.WriteMessage("\n[AVISO] " + a);
                if (planta.Ignoradas.Count > 0)
                    ed.WriteMessage($"\n{planta.Ignoradas.Count} pedaço(s) do layer de parede ignorado(s) (escada, mureta, marco): estão no relatório.");
                double ajuste = GradeModular.Ajustar(planta);
                if (ajuste > 1.5)
                    ed.WriteMessage($"\n[AVISO] Eixos movidos até {ajuste:0.#} cm para a grade de 5 cm: a arquitetura não está modulada em algum ponto.");
                Numeracao.Numerar(planta);
                return new Leitura { Planta = planta, Leitor = leitor, AjusteGrade = ajuste };
            }
            return null;
        }

        private static OpcoesDeteccao OpcoesDeteccao(ConfigObra cfg)
        {
            var op = new OpcoesDeteccao();
            foreach (var l in cfg.LayersParede) op.LayersParede.Add(l);
            foreach (var l in cfg.LayersPorta) op.LayersPorta.Add(l);
            foreach (var l in cfg.LayersJanela) op.LayersJanela.Add(l);
            foreach (var x in cfg.Exclusoes) op.Exclusoes.Add((x.X, x.Y));
            return op;
        }

        /// <summary>
        /// Quadro de janelas: o desenho só dá a largura (e às vezes errada). Para cada largura desenhada o projetista
        /// informa "medida x altura / peitoril" (ex.: 150 x 100 / 120), que vai na anotação da abertura e nas cotas.
        /// </summary>
        private static bool PerguntarJanelas(Editor ed, PlantaDetectada planta, ConfigObra cfg)
        {
            var grupos = planta.Paredes.SelectMany(w => w.Vaos).Where(v => v.Tipo == TipoVao.Janela)
                .GroupBy(v => Perguntas.Num(Math.Round(v.Largura / 5) * 5)).OrderBy(g => g.Key).ToList();
            if (grupos.Count == 0) return true;
            ed.WriteMessage("\nJanelas desenhadas: " + string.Join(", ", grupos.Select(g => $"{g.Key} cm ({g.Count()})")) + ".");
            bool faltam = grupos.Any(g => !cfg.Janelas.ContainsKey(g.Key));
            var informar = Perguntas.SimNao(ed, "\nInformar medida x altura / peitoril das janelas (anotação e cotas)?", faltam);
            if (informar == null) return false;
            if (informar == true)
                foreach (var g in grupos)
                {
                    cfg.Janelas.TryGetValue(g.Key, out var atual);
                    var resp = Perguntas.Texto(ed, $"\nJanela desenhada com {g.Key} cm: medida x altura / peitoril (ex.: {g.Key} x 100 / 120)",
                        atual ?? g.Key);
                    if (resp == null) return false;
                    cfg.Janelas[g.Key] = resp;
                }
            AplicarQuadroJanelas(planta, cfg);
            return true;
        }

        /// <summary>A medida informada no quadro de janelas vale nas cotas (a janela não corta a 1ª/2ª fiada).</summary>
        private static void AplicarQuadroJanelas(PlantaDetectada planta, ConfigObra cfg)
        {
            foreach (var v in planta.Paredes.SelectMany(w => w.Vaos).Where(v => v.Tipo == TipoVao.Janela))
            {
                if (!cfg.Janelas.TryGetValue(Perguntas.Num(Math.Round(v.Largura / 5) * 5), out var resp)) continue;
                var primeiro = System.Text.RegularExpressions.Regex.Match(resp ?? "", @"\d+([.,]\d+)?");
                if (primeiro.Success &&
                    double.TryParse(primeiro.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double medida))
                    v.MedidaInformada = medida;
            }
        }

        // ------------------------------------------------------------------ planejamento

        private sealed class Plano
        {
            public SolucaoPavimento Solucao;
            public ModulacaoFiadas Modulacao;
            public OpcoesFiadas Opcoes;
            public Modulador Modulador;
        }

        private static Plano Planejar(ContextoObra ctx, PlantaDetectada planta, IDictionary<Encontro, bool> fixos)
        {
            var m = new Modulador(ctx.Familia);
            var op = CriarOpcoesFiadas(ctx);
            var sol = PlanejadorFiadas.Resolver(planta, m, op, fixos);
            return new Plano { Solucao = sol, Modulacao = ModulacaoPavimento.Modular(sol, m, op), Opcoes = op, Modulador = m };
        }

        /// <summary>Numeração por tipo (paredes iguais com o mesmo número) ou uma por parede, conforme a obra.</summary>
        private static void Nomear(ContextoObra ctx, PlantaDetectada planta, SolucaoPavimento sol, ModulacaoFiadas mod)
        {
            if (ctx.Config.NumeracaoPorTipo) Numeracao.NumerarPorTipo(planta.Paredes, w => Numeracao.Assinatura(sol, mod, w));
            else Numeracao.Numerar(planta);
            foreach (var t in sol.Fiada1.Trechos.Concat(sol.Fiada2.Trechos)) t.Nome = t.Parede.Nome;
        }

        private static OpcoesFiadas CriarOpcoesFiadas(ContextoObra ctx) => new OpcoesFiadas
        {
            Largura = LarguraParede(ctx),
            Junta = ctx.Familia.Junta,
            FolgaPorta = ctx.Config.FolgaPorta,
            FolgaJanela = ctx.Config.FolgaJanela,
            DeslocamentoMaximoPorta = ctx.Config.DeslocamentoMaximoPorta,
            // 54 fixo e centrado nos encontros em T e em cruz (se a obra usa o 54).
            BlocoEncontro = ctx.Familia.Pecas.FirstOrDefault(p => p.Funcao == FuncaoPeca.Encontro && p.Habilitada)
        };

        // ------------------------------------------------------------------ desenho

        /// <summary>Peças fixas (54) e peças de cada trecho da fiada; trechos com problema vão para o relatório.</summary>
        private static (int fechados, int total) DesenharFiada(Document doc, string comando, Desenhista desenhista,
            SolucaoPavimento sol, List<(TrechoFiada trecho, ResultadoModulacao resultado)> fiada, int numero, double fator)
        {
            foreach (var f in sol.Plano(numero).Fixas)
            {
                var (x1, y1) = f.Parede.Ponto(f.Inicio);
                var (x2, y2) = f.Parede.Ponto(f.Fim);
                var geo = new Trecho(new Point3d(x1 / fator, y1 / fator, 0), new Point3d(x2 / fator, y2 / fator, 0));
                desenhista.Desenhar(geo, new PecaPosicionada { Peca = f.Peca, Inicio = 0 });
            }

            int fechados = 0;
            foreach (var (t, r) in fiada)
            {
                Registro.Adicionar(doc, MemoriaCalculo.Linha(comando, t.Nome, numero, t.Comprimento, t.Pontas, r));
                var geo = Geometrico(t, fator);
                foreach (var p in r.Pecas) desenhista.Desenhar(geo, p);
                if (r.Sucesso) { fechados++; if (r.Avisos.Count == 0) continue; }
                Relatar(doc, comando, t.Nome, geo, r);
            }
            return (fechados, fiada.Count);
        }

        /// <summary>Graute nos encontros, nome das paredes, aberturas, drywall, cotas internas e título.</summary>
        private static void Anotar(Anotador an, PlantaDetectada planta, SolucaoPavimento sol, OpcoesFiadas op,
            ContextoObra ctx, bool cotar, string titulo)
        {
            foreach (var (x, y) in sol.Grautes) an.Graute(x, y);
            foreach (var w in planta.Paredes) an.Rotulo(planta, w);
            foreach (var w in planta.Paredes)
                foreach (var v in w.Vaos) an.Abertura(planta, w, v, op, ctx.Config);
            foreach (var ex in planta.Excluidas)
            {
                var marcado = ctx.Config.Exclusoes.FirstOrDefault(x =>
                    ex.orientacao == Orientacao.Horizontal
                        ? Math.Abs(x.Y - ex.coord) < 20 && x.X >= ex.inicio - 5 && x.X <= ex.fim + 5
                        : Math.Abs(x.X - ex.coord) < 20 && x.Y >= ex.inicio - 5 && x.Y <= ex.fim + 5);
                if (marcado == null || marcado.Tipo == "drywall") an.Drywall(ex);
            }
            if (cotar) an.CotasInternas(Anotacoes.CotasInternas(planta, op), op.Largura);

            double xMax = planta.Paredes.Max(w => w.Horizontal ? w.Fim : w.Coord) + op.Largura / 2;
            double yMin = planta.Paredes.Min(w => w.Horizontal ? w.Coord : w.Inicio) - op.Largura / 2;
            an.Titulo(titulo, xMax, yMin);
        }

        /// <summary>Trecho do núcleo (cm, ao longo do eixo) → trecho no desenho (unidades do desenho).</summary>
        private static Trecho Geometrico(TrechoFiada t, double fator)
        {
            var (x1, y1) = t.Parede.Ponto(t.Inicio);
            var (x2, y2) = t.Parede.Ponto(t.Fim);
            return new Trecho(new Point3d(x1 / fator, y1 / fator, 0), new Point3d(x2 / fator, y2 / fator, 0));
        }

        private sealed class Conferencia
        {
            public List<ObjectId> Eixos = new List<ObjectId>();
            public List<ObjectId> Nomes = new List<ObjectId>();
        }

        /// <summary>Eixos das paredes detectadas e números provisórios (para conferir antes de modular).</summary>
        private static Conferencia DesenharEixos(Database db, Transaction tr, PlantaDetectada planta, double fator, double escala)
        {
            var c = new Conferencia();
            var espaco = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            Camadas.Garantir(db, tr, LayerEixos, 9);
            var estilo = Camadas.Texto(db, tr);
            double altura = 2.5 * escala / (10.0 * fator); // 2,5 mm no papel

            foreach (var w in planta.Paredes)
            {
                var (x1, y1) = w.Ponto(w.Inicio);
                var (x2, y2) = w.Ponto(w.Fim);
                var eixo = new Line(new Point3d(x1 / fator, y1 / fator, 0), new Point3d(x2 / fator, y2 / fator, 0)) { Layer = LayerEixos };
                c.Eixos.Add(espaco.AppendEntity(eixo));
                tr.AddNewlyCreatedDBObject(eixo, true);

                var (mx, my) = w.Ponto((w.Inicio + w.Fim) / 2);
                double afast = 10 / fator;
                var pos = w.Horizontal ? new Point3d(mx / fator, my / fator + afast, 0) : new Point3d(mx / fator - afast, my / fator, 0);
                var texto = new DBText
                {
                    TextString = w.Nome,
                    Height = altura,
                    Layer = LayerEixos,
                    TextStyleId = estilo,
                    Rotation = w.Horizontal ? 0 : Math.PI / 2,
                    HorizontalMode = TextHorizontalMode.TextCenter,
                    VerticalMode = TextVerticalMode.TextBottom,
                    AlignmentPoint = pos,
                    Position = pos
                };
                c.Nomes.Add(espaco.AppendEntity(texto));
                tr.AddNewlyCreatedDBObject(texto, true);
                texto.AdjustAlignment(db);
            }
            return c;
        }

        // ------------------------------------------------------------------ etapas e conferência

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
        /// Rodar o ALVPLANTA de novo na mesma planta duplicaria blocos, graute, nomes, aberturas e cotas: se a seleção
        /// tem objetos de uma modulação anterior, pergunta se apaga. Devolve a seleção sem eles (ou como estava, se
        /// o usuário preferir manter), ou nulo se cancelou.
        /// </summary>
        private static ObjectId[] OferecerApagarAnterior(Document doc, ObjectId[] sel, CatalogoBlocos catalogo)
        {
            var layers = new HashSet<string>(Anotador.Layers.Concat(LayersAntigos).Concat(new[] { LayerEixos }), StringComparer.OrdinalIgnoreCase);
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
                        string nome = ((BlockTableRecord)tr.GetObject(defId, OpenMode.ForRead)).Name;
                        nosso = catalogo.BuscarPorBloco(nome) != null ||
                                Familia.NormalizarNome(nome) == Familia.NormalizarNome(Anotador.BlocoGraute);
                    }
                    if (nosso) anteriores.Add(id);
                }
            }
            if (anteriores.Count == 0) return sel;

            var apagar = Perguntas.SimNao(doc.Editor,
                $"\nA seleção já tem {anteriores.Count} objeto(s) de uma modulação anterior (blocos, graute, nomes, aberturas, " +
                "cotas, eixos). Apagar antes de modular de novo?", true);
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

        private static string Resumo(PlantaDetectada p)
        {
            var vaos = p.Paredes.SelectMany(w => w.Vaos).ToList();
            return $"Detectado: {p.Paredes.Count} paredes de {Perguntas.Num(p.EspessuraTipica)} cm; encontros L {p.Encontros.Count(e => e.Tipo == TipoEncontro.L)}, " +
                   $"T {p.Encontros.Count(e => e.Tipo == TipoEncontro.T)}, X {p.Encontros.Count(e => e.Tipo == TipoEncontro.X)}; " +
                   $"{vaos.Count(v => v.Tipo == TipoVao.Porta)} portas, {vaos.Count(v => v.Tipo == TipoVao.Janela)} janelas, " +
                   $"{vaos.Count(v => v.Tipo == TipoVao.Passagem)} passagens.";
        }

        /// <summary>Decisões do planejamento que o projetista deve conhecer (vão para o relatório).</summary>
        private static void RegistrarDecisoes(Document doc, string comando, Leitura leitura, SolucaoPavimento sol)
        {
            foreach (var a in sol.Avisos) Registro.Adicionar(doc, Gravidade.Aviso, comando, null, a);
            foreach (var a in leitura.Planta.Ignoradas) Registro.Adicionar(doc, Gravidade.Aviso, comando, null, "Ignorado: " + a);
            if (leitura.AjusteGrade > 0.05)
                Registro.Adicionar(doc, Gravidade.Aviso, comando, null,
                    $"Eixos ajustados até {leitura.AjusteGrade:0.#} cm para a grade de 5 cm (as paredes fecham com juntas de 1 cm).");
            foreach (var w in leitura.Planta.Paredes)
                foreach (var v in w.Vaos.Where(v => v.Tipo == TipoVao.Porta && Math.Abs(v.Deslocamento) >= 0.5))
                    Registro.Adicionar(doc, Gravidade.Aviso, comando, w.Nome,
                        $"Porta de {Perguntas.Num(v.Nominal)} deslocada {Perguntas.Num(Math.Round(v.Deslocamento, 1))} cm " +
                        "(ao longo da parede) para os pedaços dos dois lados fecharem com blocos inteiros.");
        }

        /// <summary>Peça desenhada da 1ª fiada: retângulo em cm e se está alinhada na horizontal.</summary>
        private sealed class PecaDesenhada
        {
            public double X0, Y0, X1, Y1;
            public bool Horizontal;
        }

        /// <summary>
        /// Blocos do catálogo na seleção, como retângulos em cm. A direção vem das medidas da peça (a bolacha é
        /// mais curta que larga e pode estar desenhada "em pé"), não da rotação do bloco.
        /// </summary>
        private static List<PecaDesenhada> PecasDesenhadas(Transaction tr, List<BlockReference> blocos,
            CatalogoBlocos catalogo, double fator)
        {
            var pecas = new List<PecaDesenhada>();
            foreach (var br in blocos)
            {
                var defId = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
                var nome = ((BlockTableRecord)tr.GetObject(defId, OpenMode.ForRead)).Name;
                var peca = catalogo.BuscarPorBloco(nome);
                if (peca == null) continue;

                var cantos = Geometria.Cantos(br, tr);
                if (cantos.Count == 0) continue;
                double x0 = cantos.Min(c => c.X) * fator, x1 = cantos.Max(c => c.X) * fator;
                double y0 = cantos.Min(c => c.Y) * fator, y1 = cantos.Max(c => c.Y) * fator;
                double erroH = Math.Abs(x1 - x0 - peca.Comprimento) + Math.Abs(y1 - y0 - peca.Largura);
                double erroV = Math.Abs(y1 - y0 - peca.Comprimento) + Math.Abs(x1 - x0 - peca.Largura);
                if (Math.Min(erroH, erroV) > 4) continue; // inclinada ou fora de escala: fora da leitura automática
                pecas.Add(new PecaDesenhada { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Horizontal = erroH <= erroV });
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

        private static void Finalizar(Editor ed, int fechados, int total, Desenhista d, Anotador an, SolucaoPavimento sol)
        {
            ed.WriteMessage($"\n{fechados} de {total} trecho(s) fecharam exatamente; {d.PecasInseridas} peça(s), " +
                            $"{an.Grautes} ponto(s) de graute e {an.Cotas} cota(s).");
            foreach (var a in sol.Avisos.Where(a => a.Contains("simétrico"))) ed.WriteMessage("\n" + a);
            AvisarBlocos(ed, d);
            if (fechados < total)
                ed.WriteMessage("\nTrechos que não fecharam estão no relatório: ALVRELATORIO.");
            ed.WriteMessage("\nDecisões (portas deslocadas, encontros, ajustes) e a memória de cálculo: ALVRELATORIO.");
        }
    }
}

// ============================================================================
// AlvMod — Protótipo v0.1
// Modulação de 1ª e 2ª fiada de alvenaria estrutural, família de concreto 39
// Escopo desta versão: parede ÚNICA, reta, sem encontro com outras paredes.
// Detecção de porta: quebra na polilinha selecionada (gap entre segmentos).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(AlvMod.AlvModCommands))]

namespace AlvMod
{
    // ------------------------------------------------------------------
    // Modelo de peça
    // ------------------------------------------------------------------
    public class Peca
    {
        public string BlockName;   // nome do bloco no AutoCAD
        public double Comprimento; // cm
        public string Layer;
        public int Custo;          // usado na otimização — menor custo = preferência

        public Peca(string blockName, double comprimento, string layer, int custo)
        {
            BlockName = blockName;
            Comprimento = comprimento;
            Layer = layer;
            Custo = custo;
        }
    }

    public static class Catalogo
    {
        // Família 39 — concreto. Nomes de bloco batem com o padrão
        // encontrado no arquivo de referência do usuário (J Soares).
        public static readonly List<Peca> Familia39 = new List<Peca>
        {
            new Peca("P-Bloco39",     39, "BLOCO 14X39", custo: 1),
            new Peca("P-Bloco 34",    34, "BLOCO 14X34", custo: 4),  // amarração
            new Peca("P-Bloco 19",    19, "BLOCO 14X19", custo: 3),  // meio bloco
            new Peca("P-Bloco 54",    54, "BLOCO 14X54", custo: 4),  // encontro de parede
            new Peca("Bloco 9x14",     9, "BOLACHA 14X9", custo: 8), // compensador maior (não grauteável)
            new Peca("P-Bolacha 4x14", 4, "BOLACHA 14X4", custo: 10), // compensador (não grauteável)
        };

        public const double Junta = 1.0; // cm
    }

    // ------------------------------------------------------------------
    // Resultado da modulação de um trecho
    // ------------------------------------------------------------------
    public class Colocacao
    {
        public Peca Peca;
        public double PosicaoInicial; // cm, a partir do início do TRECHO
    }

    public class ResultadoTrecho
    {
        public bool Sucesso;
        public List<Colocacao> Colocacoes = new List<Colocacao>();
        public double ComprimentoTrecho;
    }

    // ------------------------------------------------------------------
    // Motor de preenchimento — programação dinâmica
    // Trabalha em cm inteiros (arredonda-se a entrada). Isso é suficiente
    // para família 39 porque todas as peças têm dimensão inteira em cm.
    // ------------------------------------------------------------------
    public static class MotorModulacao
    {
        public static ResultadoTrecho Preencher(double comprimentoTrecho, List<Peca> catalogo)
        {
            var resultado = new ResultadoTrecho { ComprimentoTrecho = comprimentoTrecho };

            int L = (int)Math.Round(comprimentoTrecho);
            if (L <= 0)
            {
                resultado.Sucesso = true; // trecho vazio (ex.: vão de porta) — nada a preencher
                return resultado;
            }

            // Alvo em "unidades de peça+junta": ocupar exatamente L, sabendo que
            // cada peça consome (comprimento + junta), exceto a última que não
            // tem junta depois dela. Modelamos como alvo = L + junta, somando
            // sempre peça+junta, e descontamos 1 junta no final.
            int junta = (int)Catalogo.Junta;
            int alvo = L + junta;

            // dp[i] = menor custo para ocupar exatamente i cm (em unidades peça+junta),
            // ou -1 se impossível
            int[] dp = new int[alvo + 1];
            int[] escolha = new int[alvo + 1]; // índice da peça usada para chegar em i
            for (int i = 0; i <= alvo; i++) { dp[i] = int.MaxValue; escolha[i] = -1; }
            dp[0] = 0;

            for (int i = 0; i <= alvo; i++)
            {
                if (dp[i] == int.MaxValue) continue;
                for (int p = 0; p < catalogo.Count; p++)
                {
                    int passo = (int)catalogo[p].Comprimento + junta;
                    int prox = i + passo;
                    if (prox <= alvo && dp[i] + catalogo[p].Custo < dp[prox])
                    {
                        dp[prox] = dp[i] + catalogo[p].Custo;
                        escolha[prox] = p;
                    }
                }
            }

            if (dp[alvo] == int.MaxValue)
            {
                resultado.Sucesso = false;
                return resultado;
            }

            // Reconstrução
            var sequencia = new List<int>();
            int cur = alvo;
            while (cur > 0)
            {
                int p = escolha[cur];
                sequencia.Add(p);
                cur -= (int)catalogo[p].Comprimento + junta;
            }
            sequencia.Reverse();

            double pos = 0;
            foreach (var p in sequencia)
            {
                resultado.Colocacoes.Add(new Colocacao { Peca = catalogo[p], PosicaoInicial = pos });
                pos += catalogo[p].Comprimento + junta;
            }

            resultado.Sucesso = true;
            return resultado;
        }
    }

    // ------------------------------------------------------------------
    // Comandos
    // ------------------------------------------------------------------
    public class AlvModCommands
    {
        [CommandMethod("ALV1FIADA")]
        public void Alv1Fiada()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            // 1) Escala/unidade
            var pdi = new PromptDoubleOptions("\nFator para converter o desenho para CENTÍMETROS (1 se já está em cm): ")
            {
                DefaultValue = 1.0,
                AllowNegative = false,
                AllowZero = false
            };
            var fatorRes = ed.GetDouble(pdi);
            if (fatorRes.Status != PromptStatus.OK) return;
            double fatorCm = fatorRes.Value;

            // 2) Seleção da(s) polilinha(s) do eixo da parede (pode ser mais de um
            //    segmento, se a parede estiver fragmentada por uma porta no meio)
            var peo = new PromptEntityOptions("\nSelecione o primeiro segmento do eixo da parede: ");
            peo.SetRejectMessage("\nSelecione uma LINE ou LWPOLYLINE.");
            peo.AddAllowedClass(typeof(Line), true);
            peo.AddAllowedClass(typeof(Polyline), true);

            var segmentos = new List<(Point3d ini, Point3d fim)>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                bool continuar = true;
                while (continuar)
                {
                    var per = ed.GetEntity(peo);
                    if (per.Status != PromptStatus.OK) break;

                    var ent = tr.GetObject(per.ObjectId, OpenMode.ForRead);
                    if (ent is Line ln)
                    {
                        segmentos.Add((ln.StartPoint, ln.EndPoint));
                    }
                    else if (ent is Polyline pl)
                    {
                        for (int i = 0; i < pl.NumberOfVertices - 1; i++)
                            segmentos.Add((pl.GetPoint3dAt(i), pl.GetPoint3dAt(i + 1)));
                    }

                    var keepGoing = ed.GetKeywords(new PromptKeywordOptions(
                        "\nSelecionar outro segmento (parede cortada por porta)? [Sim/Não]", "Sim Não")
                    { AllowNone = true, Keywords = { Default = "Não" } });
                    continuar = keepGoing.Status == PromptStatus.OK && keepGoing.StringResult == "Sim";
                }
                tr.Commit();
            }

            if (segmentos.Count == 0)
            {
                ed.WriteMessage("\nNenhum segmento selecionado. Comando cancelado.");
                return;
            }

            // 3) Ordena segmentos ao longo do eixo dominante (X ou Y) e converte para cm
            bool eixoX = Math.Abs(segmentos[0].fim.X - segmentos[0].ini.X) >=
                         Math.Abs(segmentos[0].fim.Y - segmentos[0].ini.Y);

            var trechosOrdenados = segmentos
                .Select(s => new
                {
                    Ini = ProjetarNoEixo(s.ini, eixoX) * fatorCm,
                    Fim = ProjetarNoEixo(s.fim, eixoX) * fatorCm,
                    Ponto3dIni = s.ini,
                    Ponto3dFim = s.fim
                })
                .Select(s => s.Ini <= s.Fim
                    ? new { Ini = s.Ini, Fim = s.Fim, P0 = s.Ponto3dIni, P1 = s.Ponto3dFim }
                    : new { Ini = s.Fim, Fim = s.Ini, P0 = s.Ponto3dFim, P1 = s.Ponto3dIni })
                .OrderBy(s => s.Ini)
                .ToList();

            ed.WriteMessage($"\n{trechosOrdenados.Count} trecho(s) sólido(s) identificado(s).");

            // 4) Garante layers e blocos, então modula e desenha cada trecho
            using (var tr = db.TransactionManager.StartTransaction())
            {
                GarantirLayersEBlocos(db, tr, Catalogo.Familia39);

                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                int trechoIdx = 0;
                foreach (var trecho in trechosOrdenados)
                {
                    trechoIdx++;
                    double comprimentoTrecho = trecho.Fim - trecho.Ini;
                    var resultado = MotorModulacao.Preencher(comprimentoTrecho, Catalogo.Familia39);

                    if (!resultado.Sucesso)
                    {
                        ed.WriteMessage($"\n[INCONSISTÊNCIA] Trecho {trechoIdx}: {comprimentoTrecho:F1} cm " +
                                         "não fecha com as peças da família 39. Trecho NÃO modulado — ajuste manual necessário.");
                        continue;
                    }

                    // Direção real do trecho no desenho (para posicionar no eixo certo)
                    Vector3d direcao = (trecho.P1 - trecho.P0).GetNormal();
                    double rotacao = Math.Atan2(direcao.Y, direcao.X);

                    foreach (var col in resultado.Colocacoes)
                    {
                        // Ponto de inserção em cm ao longo do trecho, convertido de volta
                        // para as unidades do desenho original.
                        double distNoDesenho = col.PosicaoInicial / fatorCm;
                        Point3d origemTrechoDesenho = trecho.P0;
                        Point3d insercao = origemTrechoDesenho + direcao * distNoDesenho;

                        InserirBloco(db, tr, ms, bt, col.Peca, insercao, rotacao);
                    }

                    ed.WriteMessage($"\nTrecho {trechoIdx}: {resultado.Colocacoes.Count} peça(s) inserida(s).");
                }

                tr.Commit();
            }

            ed.WriteMessage("\nALV1FIADA concluído. Confira o resultado antes de rodar ALV2FIADA.");
        }

        [CommandMethod("ALV2FIADA")]
        public void Alv2Fiada()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            var pdo = new PromptDoubleOptions("\nDefasagem desejada entre fiadas, em cm (padrão = meio bloco de 39 = 19.5 + 0.5 junta): ")
            {
                DefaultValue = 20.0,
                AllowNegative = false
            };
            var defRes = ed.GetDouble(pdo);
            if (defRes.Status != PromptStatus.OK) return;
            double defasagem = defRes.Value;

            var pso = new PromptSelectionOptions { MessageForAdding = "\nSelecione os blocos da 1ª fiada já desenhados: " };
            var sel = ed.GetSelection(pso);
            if (sel.Status != PromptStatus.OK) return;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pecasPorNome = Catalogo.Familia39.ToDictionary(p => p.BlockName, p => p);

                // Lê as posições da 1ª fiada
                var lidos = new List<(Point3d pos, double rot, Peca peca)>();
                foreach (SelectedObject so in sel.Value)
                {
                    var br = tr.GetObject(so.ObjectId, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;
                    var bd = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                    if (bd == null || !pecasPorNome.ContainsKey(bd.Name)) continue;

                    lidos.Add((br.Position, br.Rotation, pecasPorNome[bd.Name]));
                }

                if (lidos.Count == 0)
                {
                    ed.WriteMessage("\nNenhum bloco reconhecido da família 39 na seleção. Comando cancelado.");
                    return;
                }

                // Agrupa por direção (assume um único trecho reto por chamada,
                // igual ao ALV1FIADA desta versão de protótipo)
                double rotacaoRef = lidos[0].rot;
                Vector3d direcao = new Vector3d(Math.Cos(rotacaoRef), Math.Sin(rotacaoRef), 0);

                var ordenados = lidos.OrderBy(l => (l.pos - lidos[0].pos).DotProduct(direcao)).ToList();
                Point3d origem = ordenados[0].pos;
                double comprimentoTotal = (ordenados.Last().pos - origem).DotProduct(direcao)
                                           + ordenados.Last().peca.Comprimento;

                // 2ª fiada: começa deslocada pela defasagem, e o primeiro trecho
                // (de tamanho = defasagem) é fechado como um preenchimento próprio;
                // o restante segue a modulação padrão.
                var ed2 = new List<Colocacao>();
                var inicio = MotorModulacao.Preencher(defasagem, Catalogo.Familia39);
                double restante = comprimentoTotal - defasagem;

                if (!inicio.Sucesso || restante < 0)
                {
                    ed.WriteMessage("\n[INCONSISTÊNCIA] Defasagem informada não fecha com as peças disponíveis. " +
                                    "Ajuste o valor de defasagem e rode ALV2FIADA novamente.");
                    return;
                }

                var resto = MotorModulacao.Preencher(restante, Catalogo.Familia39);
                if (!resto.Sucesso)
                {
                    ed.WriteMessage("\n[INCONSISTÊNCIA] Trecho restante da 2ª fiada não fecha. Ajuste manual necessário.");
                    return;
                }

                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                GarantirLayersEBlocos(db, tr, Catalogo.Familia39);

                // Deslocamento visual da 2ª fiada no papel, perpendicular à parede,
                // só para não desenhar em cima da 1ª (ajuste conforme seu padrão de prancha)
                Vector3d normal = new Vector3d(-direcao.Y, direcao.X, 0);
                double offsetVisual = 60.0; // cm — ajuste livre

                foreach (var col in inicio.Colocacoes)
                {
                    Point3d p = origem + direcao * col.PosicaoInicial + normal * offsetVisual;
                    InserirBloco(db, tr, ms, bt, col.Peca, p, rotacaoRef);
                }
                foreach (var col in resto.Colocacoes)
                {
                    Point3d p = origem + direcao * (defasagem + col.PosicaoInicial) + normal * offsetVisual;
                    InserirBloco(db, tr, ms, bt, col.Peca, p, rotacaoRef);
                }

                tr.Commit();
                ed.WriteMessage($"\nALV2FIADA concluído: {inicio.Colocacoes.Count + resto.Colocacoes.Count} peça(s) inserida(s).");
            }
        }

        // ------------------------------------------------------------------
        // Utilitários
        // ------------------------------------------------------------------
        private static double ProjetarNoEixo(Point3d p, bool eixoX) => eixoX ? p.X : p.Y;

        private static void GarantirLayersEBlocos(Database db, Transaction tr, List<Peca> catalogo)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
            foreach (var peca in catalogo)
            {
                if (!lt.Has(peca.Layer))
                {
                    var ltr = new LayerTableRecord { Name = peca.Layer };
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
            }

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
            foreach (var peca in catalogo)
            {
                if (bt.Has(peca.BlockName)) continue;

                // Bloco não existe no desenho ainda — cria um placeholder simples
                // (retângulo 14 x comprimento). Substitua depois pela sua biblioteca real.
                var btr = new BlockTableRecord { Name = peca.BlockName };
                bt.Add(btr);
                tr.AddNewlyCreatedDBObject(btr, true);

                using (var pl = new Polyline())
                {
                    pl.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(peca.Comprimento, 0), 0, 0, 0);
                    pl.AddVertexAt(2, new Point2d(peca.Comprimento, 14), 0, 0, 0);
                    pl.AddVertexAt(3, new Point2d(0, 14), 0, 0, 0);
                    pl.Closed = true;
                    pl.Layer = "0"; // herda a cor/layer do INSERT
                    btr.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);
                }
            }
        }

        private static void InserirBloco(Database db, Transaction tr, BlockTableRecord ms,
            BlockTable bt, Peca peca, Point3d posicao, double rotacao)
        {
            var br = new BlockReference(posicao, bt[peca.BlockName])
            {
                Layer = peca.Layer,
                Rotation = rotacao
            };
            ms.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
        }
    }
}

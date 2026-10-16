using System;
using System.Collections.Generic;
using System.Linq;
using AlvMod.Core.Catalogo;
using AlvMod.Core.Modulacao;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AlvMod.Plugin.Desenho
{
    /// <summary>Desenha peças e cotas de uma fiada.</summary>
    internal sealed class Desenhista
    {
        public const string LayerCotas = "COTAS ALVENARIA";

        private readonly Database _db;
        private readonly Transaction _tr;
        private readonly BlockTableRecord _espaco;
        private readonly double _fator;
        private readonly Vector3d _deslocamento;

        /// <summary>Blocos que não existiam no desenho e foram criados como retângulo provisório.</summary>
        public HashSet<string> BlocosProvisorios { get; } = new HashSet<string>();

        public int PecasInseridas { get; private set; }

        /// <param name="fator">Quantos cm vale 1 unidade do desenho (1 = cm, 100 = m, 0.1 = mm).</param>
        /// <param name="deslocamento">Translação aplicada a tudo que for desenhado (planta da 2ª fiada).</param>
        public Desenhista(Database db, Transaction tr, double fator, Vector3d deslocamento = default)
        {
            _db = db;
            _tr = tr;
            _fator = fator;
            _deslocamento = deslocamento;
            _espaco = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
        }

        /// <summary>
        /// Insere o bloco DWG da peça. Convenção (a mesma do protótipo v0.1): bloco desenhado em cm,
        /// ponto base no canto inferior esquerdo, comprimento ao longo do X e largura no Y — a peça fica
        /// centrada no eixo da parede. Se o bloco não existir no desenho, é criado um bloco provisório
        /// (retângulo) com o mesmo nome; ao inserir a biblioteca real, os provisórios são substituídos.
        /// </summary>
        public void Desenhar(Trecho trecho, PecaPosicionada item)
        {
            var peca = item.Peca;
            string layer = GarantirLayer(peca.Layer, peca.Cor);
            var bloco = GarantirBloco(peca);

            double meia = peca.Largura / 2.0 / _fator;
            var canto = trecho.PontoEm(item.Inicio / _fator) - trecho.Perpendicular * meia + _deslocamento;

            var br = new BlockReference(canto, bloco)
            {
                Rotation = Math.Atan2(trecho.Direcao.Y, trecho.Direcao.X),
                ScaleFactors = new Scale3d(1.0 / _fator),
                Layer = layer
            };
            Adicionar(br);
            PecasInseridas++;
        }

        /// <summary>
        /// Cotas alinhadas em cadeia ao longo da face da parede: cada trecho de alvenaria e cada vão entre eles
        /// (parede até o início do vão, o vão inteiro, do vão até o fim da parede — questionário, item 47).
        /// </summary>
        /// <param name="escala">Denominador da escala de impressão (50 para 1:50).</param>
        /// <param name="larguraCm">Largura da parede: as cotas medem pela face, não pelo eixo.</param>
        public int Cotar(IList<Trecho> trechos, double escala, double larguraCm)
        {
            if (trechos.Count == 0) return 0;
            var refT = trechos[0];

            // Pontas de todos os trechos, ordenadas ao longo da parede.
            var pontos = trechos.SelectMany(t => new[] { t.Inicio, t.Fim })
                .OrderBy(p => (p - refT.Inicio).DotProduct(refT.Direcao))
                .ToList();

            // Tamanhos pensados em papel (mm) e convertidos para o desenho: dimscale = escala / (10 × fator).
            double dimscale = escala / (10.0 * _fator);
            double afastamento = 8.0 * dimscale; // 8 mm no papel, abaixo/à direita da parede
            var face = -refT.Perpendicular * (larguraCm / 2.0 / _fator); // face do lado da cota
            var lado = -refT.Perpendicular * afastamento;
            string layer = GarantirLayer(LayerCotas, 2);

            int n = 0;
            for (int i = 0; i + 1 < pontos.Count; i++)
            {
                if (pontos[i].DistanceTo(pontos[i + 1]) < 1e-6) continue;
                var p1 = pontos[i] + face + _deslocamento;
                var p2 = pontos[i + 1] + face + _deslocamento;
                var cota = new AlignedDimension(p1, p2, p1 + lado, "", _db.Dimstyle)
                {
                    Layer = layer,
                    Dimscale = dimscale
                };
                Adicionar(cota);
                n++;
            }
            return n;
        }

        private void Adicionar(Entity ent)
        {
            _espaco.AppendEntity(ent);
            _tr.AddNewlyCreatedDBObject(ent, true);
        }

        private ObjectId GarantirBloco(Peca peca)
        {
            var bt = (BlockTable)_tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(peca.NomeBlocoDwg)) return bt[peca.NomeBlocoDwg];

            bt.UpgradeOpen();
            var btr = new BlockTableRecord { Name = peca.NomeBlocoDwg, Origin = Point3d.Origin };
            var id = bt.Add(btr);
            _tr.AddNewlyCreatedDBObject(btr, true);

            var ret = new Polyline(4) { Closed = true, Layer = "0" }; // layer 0: herda o layer do INSERT
            ret.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
            ret.AddVertexAt(1, new Point2d(peca.Comprimento, 0), 0, 0, 0);
            ret.AddVertexAt(2, new Point2d(peca.Comprimento, peca.Largura), 0, 0, 0);
            ret.AddVertexAt(3, new Point2d(0, peca.Largura), 0, 0, 0);
            btr.AppendEntity(ret);
            _tr.AddNewlyCreatedDBObject(ret, true);

            BlocosProvisorios.Add(peca.NomeBlocoDwg);
            return id;
        }

        private string GarantirLayer(string nome, short cor)
        {
            var lt = (LayerTable)_tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(nome)) return nome; // layer existente: mantém a cor do escritório

            lt.UpgradeOpen();
            var novo = new LayerTableRecord
            {
                Name = nome,
                Color = Color.FromColorIndex(ColorMethod.ByAci, cor is >= 1 and <= 255 ? cor : (short)7)
            };
            lt.Add(novo);
            _tr.AddNewlyCreatedDBObject(novo, true);
            return nome;
        }
    }
}

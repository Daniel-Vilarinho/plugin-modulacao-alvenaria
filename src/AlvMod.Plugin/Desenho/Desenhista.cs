using System;
using System.Collections.Generic;
using AlvMod.Core.Catalogo;
using AlvMod.Core.Modulacao;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AlvMod.Plugin.Desenho
{
    /// <summary>Desenha as peças de uma fiada ao longo de um trecho.</summary>
    internal sealed class Desenhista
    {
        private readonly Database _db;
        private readonly Transaction _tr;
        private readonly BlockTableRecord _espaco;
        private readonly double _fator;

        /// <summary>Blocos que não existiam no desenho e foram criados como retângulo provisório.</summary>
        public HashSet<string> BlocosProvisorios { get; } = new HashSet<string>();

        public int PecasInseridas { get; private set; }

        /// <param name="fator">Quantos cm vale 1 unidade do desenho (1 = cm, 100 = m, 0.1 = mm).</param>
        public Desenhista(Database db, Transaction tr, double fator)
        {
            _db = db;
            _tr = tr;
            _fator = fator;
            _espaco = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
        }

        /// <summary>
        /// Insere o bloco DWG da peça. Convenção (a mesma do protótipo v0.1): bloco desenhado em cm,
        /// ponto base no canto inferior esquerdo, comprimento ao longo do X e largura no Y — a peça fica
        /// centrada no eixo da parede. Se o bloco não existir no desenho, é criado um bloco provisório
        /// (retângulo) com o mesmo nome; ao inserir a biblioteca real, os provisórios são substituídos.
        /// </summary>
        public void Desenhar(Trecho trecho, PecaPosicionada item, string sufixoLayer)
        {
            var peca = item.Peca;
            string layer = GarantirLayer(peca.Layer + sufixoLayer);
            var bloco = GarantirBloco(peca);

            double meia = peca.Largura / 2.0 / _fator;
            var canto = trecho.Inicio + trecho.Direcao * (item.Inicio / _fator) - trecho.Perpendicular * meia;

            var br = new BlockReference(canto, bloco)
            {
                Rotation = Math.Atan2(trecho.Direcao.Y, trecho.Direcao.X),
                ScaleFactors = new Scale3d(1.0 / _fator),
                Layer = layer
            };
            Adicionar(br);
            PecasInseridas++;
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

        private void Adicionar(Entity ent)
        {
            _espaco.AppendEntity(ent);
            _tr.AddNewlyCreatedDBObject(ent, true);
        }

        private string GarantirLayer(string nome)
        {
            var lt = (LayerTable)_tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(nome)) return nome;

            lt.UpgradeOpen();
            var novo = new LayerTableRecord { Name = nome };
            lt.Add(novo);
            _tr.AddNewlyCreatedDBObject(novo, true);
            return nome;
        }
    }
}

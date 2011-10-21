using System;

namespace SIL.APRE.FeatureModel.Fluent
{
	public class FeatureSystemBuilder : IFeatureSystemSyntax
	{
		private readonly FeatureSystem _featSys;

		public FeatureSystemBuilder()
		{
			_featSys = new FeatureSystem();
		}

		public IFeatureSystemSyntax SymbolicFeature(string id, string desc, Func<ISymbolicFeatureSyntax, ISymbolicFeatureSyntax> build)
		{
			var featureBuilder = new SymbolicFeatureBuilder(id, desc);
			_featSys.AddFeature(featureBuilder.Value);
			build(featureBuilder);
			return this;
		}

		public IFeatureSystemSyntax SymbolicFeature(string id, Func<ISymbolicFeatureSyntax, ISymbolicFeatureSyntax> build)
		{
			var featureBuilder = new SymbolicFeatureBuilder(id);
			_featSys.AddFeature(featureBuilder.Value);
			build(featureBuilder);
			return this;
		}

		public IFeatureSystemSyntax StringFeature(string id, string desc)
		{
			Feature feature = new StringFeature(id) {Description = desc};
			_featSys.AddFeature(feature);
			return this;
		}

		public IFeatureSystemSyntax StringFeature(string id)
		{
			Feature feature = new StringFeature(id);
			_featSys.AddFeature(feature);
			return this;
		}

		public IFeatureSystemSyntax StringFeature(string id, string desc, Func<IStringFeatureSyntax, IStringFeatureSyntax> build)
		{
			var featureBuilder = new StringFeatureBuilder(id, desc);
			_featSys.AddFeature(featureBuilder.Value);
			build(featureBuilder);
			return this;
		}

		public IFeatureSystemSyntax StringFeature(string id, Func<IStringFeatureSyntax, IStringFeatureSyntax> build)
		{
			var featureBuilder = new StringFeatureBuilder(id);
			_featSys.AddFeature(featureBuilder.Value);
			build(featureBuilder);
			return this;
		}

		public IFeatureSystemSyntax ComplexFeature(string id, string desc, Func<IComplexFeatureSyntax, IComplexFeatureSyntax> build)
		{
			var featureBuilder = new ComplexFeatureBuilder(_featSys, id, desc);
			_featSys.AddFeature(featureBuilder.Value);
			build(featureBuilder);
			return this;
		}

		public IFeatureSystemSyntax ComplexFeature(string id, Func<IComplexFeatureSyntax, IComplexFeatureSyntax> build)
		{
			var featureBuilder = new ComplexFeatureBuilder(_featSys, id);
			_featSys.AddFeature(featureBuilder.Value);
			build(featureBuilder);
			return this;
		}

		public IFeatureSystemSyntax ExtantFeature(string id)
		{
			_featSys.AddFeature(_featSys.GetFeature(id));
			return this;
		}

		public FeatureSystem Value
		{
			get { return _featSys; }
		}
	}
}

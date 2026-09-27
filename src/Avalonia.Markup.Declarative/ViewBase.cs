using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.Collections.Immutable;
using System.Diagnostics;

namespace Avalonia.Markup.Declarative;

/// <summary>
/// Defines when a view should be initialized.
/// </summary>
public enum ViewInitializationStrategy
{
    /// <summary>
    /// View is initialized lazily when first accessed (e.g., when Child property is accessed or when attached to visual tree).
    /// </summary>
    Lazy,

    /// <summary>
    /// View is initialized immediately in the constructor.
    /// </summary>
    Immediate
}

/// <summary>Base class for declarative views whose UI is built from a view model.</summary>
/// <typeparam name="TViewModel">The view model type used to build the view.</typeparam>
public abstract class ViewBase<TViewModel> : ViewBase
    where TViewModel : class
{
    /// <summary>Gets or sets the view model exposed as this view's data context.</summary>
    public virtual TViewModel? ViewModel
    {
        get => (TViewModel?)DataContext!;
        set => DataContext = value;
    }

    /// <summary>Creates and immediately initializes the view with the supplied view model.</summary>
    /// <param name="viewModel">The view model used to build the view.</param>
    protected ViewBase(TViewModel viewModel) : base(ViewInitializationStrategy.Lazy)
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Initialize();
    }

    /// <summary>Creates a view for the classic MVVM pattern, where the data context is assigned later. Initialization is deferred until a compatible data context is assigned or the view is first accessed or attached to the visual tree.</summary>
    protected ViewBase() : base(ViewInitializationStrategy.Lazy)
    {
    }

    /// <inheritdoc/>
    /// <remarks>Initializes the view once a compatible view model is assigned and the view has not already been initialized.</remarks>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is TViewModel && !IsInitialized)
        {
            Initialize();
        }
    }

    /// <summary>Builds the control tree for the supplied view model.</summary>
    /// <param name="vm">The view model whose state is displayed.</param>
    /// <returns>The root control of the view.</returns>
    protected abstract object Build(TViewModel vm);

    /// <inheritdoc/>
    protected override object Build()
    {
        if (DataContext is not TViewModel vm)
        {
            throw new InvalidOperationException(
                $"Cannot build view {GetType().Name} without a valid ViewModel of type {typeof(TViewModel).Name}.");
        }

        return Build(vm);
    }
}

/// <summary>
/// Base view class used like UserControl in XAML
/// </summary>
public abstract class ViewBase : Decorator, IReloadable, IDeclarativeViewBase
{
    private INameScope? _nameScope;

    /// <summary>
    /// Current NameScope of this view
    /// </summary>
    protected INameScope Scope => _nameScope ??= new NameScope();

    /// <summary>Raised after the view's styles and control tree have been initialized.</summary>
    public event Action? ViewInitialized;

    /// <summary>Builds the root control of the view.</summary>
    /// <returns>The root control of the view.</returns>
    protected abstract object Build();

    /// <summary>Builds the styles applied to this view.</summary>
    /// <returns>The styles to apply, or <see langword="null"/> when the view has none.</returns>
    protected virtual StyleGroup? BuildStyles() => null;

    /// <summary>Creates a view using the configured default initialization strategy.</summary>
    protected ViewBase()
        : this(AppBuilderExtensions.DefaultViewInitializationStrategy)
    {
    }

    /// <summary>Creates a view with the specified initialization strategy.</summary>
    /// <param name="initializationStrategy">Determines when the view builds its control tree.</param>
    protected ViewBase(ViewInitializationStrategy initializationStrategy)
    {
        InitializationStrategy = initializationStrategy;

        if (initializationStrategy == ViewInitializationStrategy.Immediate)
            Initialize();
    }

    /// <summary>
    /// Gets the initialization strategy used by this view.
    /// </summary>
    public ViewInitializationStrategy InitializationStrategy { get; }

    private bool _isInitialized;
    private bool _isInitializing;

    private void EnsureInitialized()
    {
        if (!_isInitialized && !_isInitializing)
        {
            Initialize();
        }
    }

    /// <summary>
    /// Called from constructor, right before initialization and building UI
    /// Override this method when you want to run some stuff before creation of children controls
    /// </summary>
    protected virtual void OnCreated()
    {
    }

    /// <summary>Runs after the view has initialized its styles and control tree.</summary>
    protected virtual void OnAfterInitialized()
    {
    }

    /// <summary>Builds the view's styles and control tree unless it is already initialized.</summary>
    public void Initialize()
    {
        if (_isInitialized || _isInitializing)
            return;

        _isInitializing = true;

        try
        {
            OnCreated();
            NameScope.SetNameScope(this, Scope);

            if (BuildStyles() is { } styleGroup)
            {
                var viewStyles = StyleBuilder.StylesToRange(styleGroup).ToImmutableList();
                Styles.AddRange(viewStyles);
            }

            var content = Build();
            Child = content as Control;

            _isInitialized = true;
            ViewInitialized?.Invoke();
            OnAfterInitialized();
        }
        catch (ViewBuildingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            Debug.WriteLine(ex.StackTrace);
            var message = CreateBuildErrorMessage(GetType(), ex);
            // A ViewBuildingException coming from a _set call is already recorded at its creation site
            // (handled by the catch above), so only the generic wrap is recorded here.
            Diagnostics.DiagnosticsErrorLog.Record(
                Diagnostics.DiagnosticSeverity.Error, Diagnostics.DiagnosticCategory.Build, GetType().Name, message);
            throw new ViewBuildingException(message, ex);
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private static string CreateBuildErrorMessage(Type viewType, Exception exception)
    {
        var rootCause = GetRootCause(exception);
        var message = $"Build error in {viewType.Name} ({rootCause.GetType().Name}): {rootCause.Message}";

        if (IsBinaryCompatibilityException(rootCause))
        {
            message += " This usually means an external package was built against a different version of Avalonia or another dependency. Check the package's declared dependency versions against the versions resolved by the app.";
        }

        return message;
    }

    private static Exception GetRootCause(Exception exception)
    {
        while (exception.InnerException != null)
        {
            exception = exception.InnerException;
        }

        return exception;
    }

    private static bool IsBinaryCompatibilityException(Exception exception) =>
        exception is MissingFieldException or MissingMethodException or TypeLoadException or System.IO.FileLoadException;

    /// <summary>
    /// Gets the child control, ensuring initialization if needed.
    /// </summary>
    public new Control? Child
    {
        get
        {
            EnsureInitialized();
            return base.Child;
        }
        protected set => base.Child = value;
    }

    #region Hot reload stuff
    /// <summary>Rebuilds the view on the UI thread and invalidates its layout and rendering.</summary>
    public void Reload()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            OnBeforeReload();
            SetValue(ChildProperty, null);
            VisualChildren.Clear();
            _nameScope = null;
            _isInitialized = false;
            _isInitializing = false;

            Initialize();

            InvalidateArrange();
            InvalidateMeasure();
            InvalidateVisual();
        });
    }

    /// <summary>Runs immediately before this view is rebuilt.</summary>
    protected virtual void OnBeforeReload()
    {
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        HotReloadManager.RegisterInstance(this);
        EnsureInitialized();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        HotReloadManager.UnregisterInstance(this);
    }
    #endregion
}

/// <summary>Reports an exception that occurred while building a declarative view.</summary>
/// <param name="message">A message describing the view-build failure.</param>
/// <param name="innerException">The exception that caused the failure.</param>
public class ViewBuildingException(string message, Exception innerException) : Exception(message, innerException);

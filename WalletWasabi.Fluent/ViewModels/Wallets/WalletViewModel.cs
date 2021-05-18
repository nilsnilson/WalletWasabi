using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using NBitcoin;
using WalletWasabi.Fluent.ViewModels.Navigation;
using System.Windows.Input;
using WalletWasabi.Fluent.ViewModels.Wallets.Home.History;
using WalletWasabi.Fluent.ViewModels.Wallets.Home.Tiles;
using WalletWasabi.Fluent.ViewModels.Wallets.Receive;
using WalletWasabi.Fluent.ViewModels.Wallets.Send;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.ViewModels.Wallets
{
	public partial class WalletViewModel : WalletViewModelBase
	{
		[AutoNotify] private IList<TileViewModel>? _tiles;
		[AutoNotify] private IList<TileLayoutViewModel>? _layouts;
		[AutoNotify] private int _layoutIndex;

		protected WalletViewModel(Wallet wallet) : base(wallet)
		{
			Disposables = Disposables is null
				? new CompositeDisposable()
				: throw new NotSupportedException($"Cannot open {GetType().Name} before closing it.");

			var balanceChanged =
				Observable.FromEventPattern(
						Wallet.TransactionProcessor,
						nameof(Wallet.TransactionProcessor.WalletRelevantTransactionProcessed))
					.Select(_ => Unit.Default)
					.Merge(Observable.FromEventPattern(Wallet, nameof(Wallet.NewFilterProcessed))
						.Select(_ => Unit.Default))
					.Merge(Services.UiConfig.WhenAnyValue(x => x.PrivacyMode).Select(_ => Unit.Default))
					.Merge(Wallet.Synchronizer.WhenAnyValue(x => x.UsdExchangeRate).Select(_ => Unit.Default))
					.Throttle(TimeSpan.FromSeconds(0.1))
					.ObserveOn(RxApp.MainThreadScheduler);

			History = new HistoryViewModel(this, balanceChanged);

			Layouts = new ObservableCollection<TileLayoutViewModel>()
			{
				new("Small", "330,330,330,330,330", "150"),
				new("Normal", "330,330,330", "150,300"),
				new("Wide", "330,330", "150,300,300")
			};

			LayoutIndex = 1;

			BalanceTile = new WalletBalanceTileViewModel(wallet, balanceChanged)
			{
				TilePresets = new ObservableCollection<TilePresetViewModel>()
				{
					new(0, 0, 1, 1),
					new(0, 0, 1, 1),
					new(0, 0, 1, 1)
				},
				TilePresetIndex = LayoutIndex
			};
			RoundStatusTile = new RoundStatusTileViewModel(wallet)
			{
				TilePresets = new ObservableCollection<TilePresetViewModel>()
				{
					new(1, 0, 1, 1),
					new(1, 0, 1, 1),
					new(1, 0, 1, 1)
				},
				TilePresetIndex = LayoutIndex
			};
			BtcPriceTile = new BtcPriceTileViewModel(wallet)
			{
				TilePresets = new ObservableCollection<TilePresetViewModel>()
				{
					new(2, 0, 1, 1),
					new(2, 0, 1, 1),
					new(0, 1, 1, 1)
				},
				TilePresetIndex = LayoutIndex
			};
			WalletPieChart = new WalletPieChartTileViewModel(wallet, balanceChanged)
			{
				TilePresets = new ObservableCollection<TilePresetViewModel>()
				{
					new(3, 0, 1, 1),
					new(0, 1, 1, 1),
					new(1, 1, 1, 1)
				},
				TilePresetIndex = LayoutIndex
			};
			BalanceChartTile = new WalletBalanceChartTileViewModel(History.UnfilteredTransactions)
			{
				TilePresets = new ObservableCollection<TilePresetViewModel>()
				{
					new(4, 0, 1, 1),
					new(1, 1, 2, 1),
					new(0, 2, 2, 1)
				},
				TilePresetIndex = LayoutIndex
			};

			_tiles = new List<TileViewModel>
			{
				BalanceTile,
				RoundStatusTile,
				BtcPriceTile,
				WalletPieChart,
				BalanceChartTile
			};

			this.WhenAnyValue(x => x.LayoutIndex)
				.Subscribe(_ => NotifyLayoutChanged());

			this.WhenAnyValue(x => x.LayoutIndex)
				.Subscribe(_ => UpdateTiles());

			SendCommand = ReactiveCommand.Create(() =>
			{
				Navigate(NavigationTarget.DialogScreen)
					.To(new SendViewModel(wallet));
			});

			ReceiveCommand = ReactiveCommand.Create(() =>
			{
				Navigate(NavigationTarget.DialogScreen)
					.To(new ReceiveViewModel(wallet));
			});
		}

		public ICommand SendCommand { get; }

		public ICommand ReceiveCommand { get; }

		private CompositeDisposable Disposables { get; set; }

		public HistoryViewModel History { get; }

		public WalletBalanceTileViewModel BalanceTile { get; }

		public RoundStatusTileViewModel RoundStatusTile { get; }

		public BtcPriceTileViewModel BtcPriceTile { get; }

		public WalletPieChartTileViewModel WalletPieChart { get; }

		public WalletBalanceChartTileViewModel BalanceChartTile { get; }

		public TileLayoutViewModel? CurrentLayout => Layouts?[LayoutIndex];

		private void NotifyLayoutChanged()
		{
			this.RaisePropertyChanged(nameof(CurrentLayout));
		}

		private void UpdateTiles()
		{
			if (Tiles != null)
			{
				foreach (var tile in Tiles)
				{
					tile.TilePresetIndex = LayoutIndex;
				}
			}
		}

		public void NavigateAndHighlight(uint256 txid)
		{
			Navigate().To(this, NavigationMode.Clear);

			RxApp.MainThreadScheduler.Schedule(async () =>
			{
				await Task.Delay(500);
				History.SelectTransaction(txid);
			});
		}

		protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables)
		{
			base.OnNavigatedTo(isInHistory, disposables);

			foreach (var tile in _tiles)
			{
				tile.Activate(disposables);
			}

			History.Activate(disposables);
		}

		public static WalletViewModel Create(Wallet wallet)
		{
			return wallet.KeyManager.IsHardwareWallet
				? new HardwareWalletViewModel(wallet)
				: wallet.KeyManager.IsWatchOnly
					? new WatchOnlyWalletViewModel(wallet)
					: new WalletViewModel(wallet);
		}
	}
}

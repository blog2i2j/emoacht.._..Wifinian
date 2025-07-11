﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static System.Math;
using ManagedNativeWifi;

namespace Wifinian.Models.Wlan;

internal class MockWorker : IWlanWorker
{
	public bool IsWorkable => true;

	#region Dispose

	private bool _disposed = false;

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (_disposed)
			return;

		_disposed = true;
	}

	#endregion

	public event EventHandler NetworkRefreshed;
	public event EventHandler<AvailabilityChangedEventArgs> AvailabilityChanged;
	public event EventHandler<InterfaceChangedEventArgs> InterfaceChanged;
	public event EventHandler<ConnectionChangedEventArgs> ConnectionChanged;
	public event EventHandler<ProfileChangedEventArgs> ProfileChanged;
	public event EventHandler<SignalQualityChangedEventArgs> SignalQualityChanged;

	private List<ProfileItem> _sourceProfiles;
	private static readonly Lazy<Random> _random = new(() => new Random());

	public async Task ScanNetworkAsync(TimeSpan timeout)
	{
		await Task.Delay(TimeSpan.FromSeconds(1)); // Dummy

		deferTask = DeferAsync(() =>
		{
			NetworkRefreshed?.Invoke(this, EventArgs.Empty);
			AvailabilityChanged?.Invoke(this, MockHelper.GetAvailabilityChangedEventArgs(Guid.Empty));
		});
	}

	public Task<IEnumerable<ProfileItem>> GetProfilesAsync()
	{
		_sourceProfiles ??= PopulateProfiles().ToList();

		_sourceProfiles
			.ForEach(x =>
			{
				if (x.Signal > 0)
					x.Signal = Max(Min(x.Signal + _random.Value.Next(-10, 10), 100), 50);
			});

		return Task.FromResult(_sourceProfiles.AsEnumerable());
	}

	public Task<bool> SetProfileOptionAsync(ProfileItem profileItem)
	{
		var targetProfile = _sourceProfiles.FirstOrDefault(x => x.Id == profileItem.Id);
		if (targetProfile is null)
			return Task.FromResult(false);

		targetProfile.IsAutoConnectEnabled = profileItem.IsAutoConnectEnabled;
		targetProfile.IsAutoSwitchEnabled = profileItem.IsAutoSwitchEnabled;

		deferTask = DeferAsync(() => ProfileChanged?.Invoke(this, MockHelper.GetProfileChangedEventArgs(targetProfile.InterfaceId)));
		return Task.FromResult(true);
	}

	public Task<bool> SetProfilePositionAsync(ProfileItem profileItem, int position)
	{
		var targetProfiles = _sourceProfiles
			.Where(x => x.InterfaceId == profileItem.InterfaceId)
			.OrderBy(x => x.Position)
			.ToList();

		if ((targetProfiles.Count == 0) || (targetProfiles.Count - 1 < position))
			return Task.FromResult(false);

		var targetProfile = targetProfiles.FirstOrDefault(x => x.Id == profileItem.Id);
		if (targetProfile is null)
			return Task.FromResult(false);

		if (targetProfile.Position == position)
			return Task.FromResult(false);

		targetProfiles.Remove(targetProfile);
		targetProfiles.Insert(position, targetProfile);

		int index = 0;
		targetProfiles.ForEach(x => x.Position = index++);

		deferTask = DeferAsync(() => ProfileChanged?.Invoke(this, MockHelper.GetProfileChangedEventArgs(targetProfile.InterfaceId)));
		return Task.FromResult(true);
	}

	public Task<bool> RenameProfileAsync(ProfileItem profileItem, string profileName)
	{
		var targetProfile = _sourceProfiles.FirstOrDefault(x => x.Id == profileItem.Id);
		if (targetProfile is null)
			return Task.FromResult(false);

		var renamedProfile = new ProfileItem(
			name: profileName,
			interfaceId: targetProfile.InterfaceId,
			interfaceName: targetProfile.InterfaceName,
			interfaceDescription: targetProfile.InterfaceDescription,
			authentication: targetProfile.Authentication,
			encryption: targetProfile.Encryption,
			isAutoConnectEnabled: targetProfile.IsAutoConnectEnabled,
			isAutoSwitchEnabled: targetProfile.IsAutoSwitchEnabled,
			position: targetProfile.Position,
			isRadioOn: targetProfile.IsRadioOn,
			isConnected: targetProfile.IsConnected,
			protocol: targetProfile.Protocol,
			signal: targetProfile.Signal,
			band: targetProfile.Band,
			channel: targetProfile.Channel);

		_sourceProfiles.Remove(targetProfile);
		_sourceProfiles.Add(renamedProfile);

		deferTask = DeferAsync(() => ProfileChanged?.Invoke(this, MockHelper.GetProfileChangedEventArgs(targetProfile.InterfaceId)));
		return Task.FromResult(true);
	}

	public Task<bool> DeleteProfileAsync(ProfileItem profileItem)
	{
		if (!_sourceProfiles.Remove(profileItem))
			return Task.FromResult(false);

		deferTask = DeferAsync(() => ProfileChanged?.Invoke(this, MockHelper.GetProfileChangedEventArgs(profileItem.InterfaceId)));
		return Task.FromResult(true);
	}

	public Task<bool> ConnectNetworkAsync(ProfileItem profileItem, TimeSpan timeout)
	{
		var targetProfiles = _sourceProfiles
			.Where(x => x.InterfaceId == profileItem.InterfaceId)
			.ToList();

		var targetProfile = targetProfiles.FirstOrDefault(x => x.Id == profileItem.Id);
		if (targetProfile is null)
			return Task.FromResult(false);

		targetProfile.IsConnected = true;
		targetProfiles.Remove(targetProfile);

		targetProfiles.ForEach(x => x.IsConnected = false);

		deferTask = DeferAsync(() => ConnectionChanged?.Invoke(this, MockHelper.GetConnectionChangedEventArgs(targetProfile.InterfaceId)));
		return Task.FromResult(true);
	}

	public Task<bool> DisconnectNetworkAsync(ProfileItem profileItem, TimeSpan timeout)
	{
		var targetProfile = _sourceProfiles.FirstOrDefault(x => x.Id == profileItem.Id);
		if (targetProfile is null)
			return Task.FromResult(false);

		targetProfile.IsConnected = false;

		deferTask = DeferAsync(() => ConnectionChanged?.Invoke(this, MockHelper.GetConnectionChangedEventArgs(targetProfile.InterfaceId)));
		return Task.FromResult(true);
	}

	#region Base

	private class InterfacePack(string name, string description, bool isRadioOn)
	{
		public Guid Id { get; } = Guid.NewGuid();
		public string Name { get; } = name;
		public string Description { get; } = description;
		public bool IsRadioOn { get; } = isRadioOn;
	}

	private ProfileItem[] PopulateProfiles()
	{
		var interfacePacks = new[]
		{
			new InterfacePack("Wi-Fi", "Intel(R) Centrino(R) Advanced-N 6205", true),
			new InterfacePack("Wi-Fi 2", "WLI-UC-GNM", true),
			new InterfacePack("Wi-Fi 3", "Marvel AVASTAR Wireless-AC Network Controller", false)
		};

		return
		[
			new ProfileItem(
				name: "Cloud7",
				interfaceId: interfacePacks[0].Id,
				interfaceName: interfacePacks[0].Name,
				interfaceDescription: interfacePacks[0].Description,
				authentication: AuthenticationMethod.WPA_Personal,
				encryption: EncryptionType.AES,
				isAutoConnectEnabled: false,
				isAutoSwitchEnabled: false,
				position: 0,
				isRadioOn: interfacePacks[0].IsRadioOn,
				isConnected: false,
				protocol: "ax",
				signal: 90,
				band: 5,
				channel: 48),

			new ProfileItem(
				name: "ESTACION",
				interfaceId: interfacePacks[0].Id,
				interfaceName: interfacePacks[0].Name,
				interfaceDescription: interfacePacks[0].Description,
				authentication: AuthenticationMethod.Open,
				encryption: EncryptionType.None,
				isAutoConnectEnabled: true,
				isAutoSwitchEnabled: false,
				position: 1,
				isRadioOn: interfacePacks[0].IsRadioOn,
				isConnected: false,
				protocol: null,
				signal: 0,
				band: 0,
				channel: 0),

			new ProfileItem(
				name: "flashair_W02",
				interfaceId: interfacePacks[0].Id,
				interfaceName: interfacePacks[0].Name,
				interfaceDescription: interfacePacks[0].Description,
				authentication: AuthenticationMethod.WPA2_Personal,
				encryption: EncryptionType.AES,
				isAutoConnectEnabled: true,
				isAutoSwitchEnabled: false,
				position: 2,
				isRadioOn: interfacePacks[0].IsRadioOn,
				isConnected: false,
				protocol: null,
				signal: 0,
				band: 0,
				channel: 0),

			new ProfileItem(
				name: "flashair_W03",
				interfaceId: interfacePacks[0].Id,
				interfaceName: interfacePacks[0].Name,
				interfaceDescription: interfacePacks[0].Description,
				authentication: AuthenticationMethod.WPA2_Personal,
				encryption: EncryptionType.AES,
				isAutoConnectEnabled: true,
				isAutoSwitchEnabled: true,
				position: 3,
				isRadioOn: interfacePacks[0].IsRadioOn,
				isConnected: false,
				protocol: "a",
				signal: 90,
				band: 2.4F,
				channel: 14),

			new ProfileItem(
				name: "Cloud7",
				interfaceId: interfacePacks[1].Id,
				interfaceName: interfacePacks[1].Name,
				interfaceDescription: interfacePacks[1].Description,
				authentication: AuthenticationMethod.WPA2_Personal,
				encryption: EncryptionType.AES,
				isAutoConnectEnabled: false,
				isAutoSwitchEnabled: false,
				position: 0,
				isRadioOn: interfacePacks[1].IsRadioOn,
				isConnected: false,
				protocol: "ax",
				signal: 70,
				band: 5,
				channel: 36),

			new ProfileItem(
				name: "nekoラン🐾🐾",
				interfaceId: interfacePacks[1].Id,
				interfaceName: interfacePacks[1].Name,
				interfaceDescription: interfacePacks[1].Description,
				authentication: AuthenticationMethod.WPA_Personal,
				encryption: EncryptionType.AES,
				isAutoConnectEnabled: true,
				isAutoSwitchEnabled: false,
				position: 1,
				isRadioOn: interfacePacks[1].IsRadioOn,
				isConnected: false,
				protocol: null,
				signal: 0,
				band: 0,
				channel: 0),

			new ProfileItem(
				name: "La La Lan...",
				interfaceId: interfacePacks[2].Id,
				interfaceName: interfacePacks[2].Name,
				interfaceDescription: interfacePacks[2].Description,
				authentication: AuthenticationMethod.Open,
				encryption: EncryptionType.None,
				isAutoConnectEnabled: true,
				isAutoSwitchEnabled: false,
				position: 0,
				isRadioOn: interfacePacks[2].IsRadioOn,
				isConnected: false,
				protocol: null,
				signal: 0,
				band: 0,
				channel: 0),
		];
	}

	private Task deferTask;

	private async Task DeferAsync(Action action)
	{
		await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
		action?.Invoke();
	}

	#endregion
}

internal static class MockHelper
{
	public static AvailabilityChangedEventArgs GetAvailabilityChangedEventArgs(Guid interfaceId)
	{
		Type type = typeof(AvailabilityChangedEventArgs);
		var constructor = type.GetConstructor([typeof(Guid), typeof(AvailabilityChangedState)]);
		return (AvailabilityChangedEventArgs)constructor.Invoke([interfaceId, default(AvailabilityChangedState)]);
	}

	public static InterfaceChangedEventArgs GetInterfaceChangedEventArgs(Guid interfaceId)
	{
		Type type = typeof(InterfaceChangedEventArgs);
		var constructor = type.GetConstructor([typeof(Guid), typeof(InterfaceChangedState)]);
		return (InterfaceChangedEventArgs)constructor.Invoke([interfaceId, default(InterfaceChangedState)]);
	}

	public static ProfileChangedEventArgs GetProfileChangedEventArgs(Guid interfaceId)
	{
		Type type = typeof(ProfileChangedEventArgs);
		var constructor = type.GetConstructor([typeof(Guid), typeof(ProfileChangedState)]);
		return (ProfileChangedEventArgs)constructor.Invoke([interfaceId, default(ProfileChangedState)]);
	}

	public static ConnectionChangedEventArgs GetConnectionChangedEventArgs(Guid interfaceId)
	{
		Type type = typeof(ConnectionChangedEventArgs);
		var constructor = type.GetConstructor([typeof(Guid), typeof(ConnectionChangedState), typeof(ConnectionNotificationData)]);
		return (ConnectionChangedEventArgs)constructor.Invoke([interfaceId, default(ConnectionChangedState), null]);
	}

	public static SignalQualityChangedEventArgs GetSignalQualityChangedEventArgs(Guid interfaceId)
	{
		Type type = typeof(SignalQualityChangedEventArgs);
		var constructor = type.GetConstructor([typeof(Guid), typeof(int)]);
		return (SignalQualityChangedEventArgs)constructor.Invoke([interfaceId, 0]);
	}
}
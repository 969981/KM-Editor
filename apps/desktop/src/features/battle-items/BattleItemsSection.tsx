/* SPDX-License-Identifier: GPL-3.0-only */
import { GameplaySettingsSection, type GameplaySettingsSectionProps } from '../gameplay-settings/GameplaySettingsSection';
import '../camera-control/CameraControlSection.css';

export function BattleItemsSection(props: Omit<GameplaySettingsSectionProps, 'battleItems' | 'cameraControl' | 'cameraHook'>) {
  return <div className="wide-panel camera-control"><GameplaySettingsSection {...props} battleItems /></div>;
}

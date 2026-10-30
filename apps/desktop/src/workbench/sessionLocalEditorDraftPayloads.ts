/* SPDX-License-Identifier: GPL-3.0-only */

export type OrdinaryFieldDraftPayload = {
  fields: Record<string, string>;
};

export type OrdinaryPokemonDraftPayload = {
  alphaMove: string | null;
  evolutionSlots: Record<
    string,
    {
      argument: string;
      form: string;
      level: string;
      method: string;
      species: string;
      action?: 'add' | 'remove';
      position?: number;
    }
  >;
  fields: Record<string, string>;
  learnsetSlots: Record<string, { level: string; moveId: string; rawLevel?: number; action?: 'add' | 'remove' }>;
};

export type OrdinaryTextDraftPayload = {
  value: string;
};

export type OrdinaryTrainerDraftPayload = {
  partyByTrainerSlot: Record<string, Record<string, string>>;
  trainerFieldsByTrainerId: Record<string, Record<string, string>>;
};

import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CampaignWizardComponent } from './campaign-wizard';

describe('CampaignWizardComponent', () => {
  let component: CampaignWizardComponent;
  let fixture: ComponentFixture<CampaignWizardComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CampaignWizardComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(CampaignWizardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
